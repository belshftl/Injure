// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

internal sealed class IlTransactionCore(
	IlWorkingBody working,
	IlSnapshot snapshot,
	InternalIlProvenance currProvenance,
	IIlManagedDelegateLowerer? managedDelegateLowerer,
	List<IDisposable> retentions
) : IStrongRefDroppable {
	private static long nextTransactionId = 0; // first will be 1 since this gets incremented upfront

	private IlWorkingBody? workingBacking = working;
	private IlSnapshot? snapshotBacking = snapshot;
	private readonly InternalIlProvenance currProvenance = currProvenance;
	private IIlManagedDelegateLowerer? managedDelegateLowerer = managedDelegateLowerer;
	private List<IDisposable>? retentionsBacking = retentions;
	private readonly HashSet<Instruction> loweredManagedInstrs = new(InstructionReferenceComparer.Instance);
	private readonly long transactionId = Interlocked.Increment(ref nextTransactionId);
	private readonly Dictionary<int, IlLabelState> labels = new();
	private readonly HashSet<int> knownLabels = new();
	private readonly List<IlInsertionEdit> insertions = new();
	private int nextLabelId;
	private int nextEditSequence;
	private bool authoringClosed;
	private bool committed;
	private bool dropped;

	private IlWorkingBody working => workingBacking ?? throw new InternalStateException("IL transaction got used after its strong refs were dropped");
	private IlSnapshot snapshot => snapshotBacking ?? throw new InternalStateException("IL transaction got used after its strong refs were dropped");
	private List<IDisposable> retentions => retentionsBacking ?? throw new InternalStateException("IL transaction got used after its strong refs were dropped");

	public string? OwnerId { get; } = currProvenance.OwnerId;
	public string? LocalId { get; } = currProvenance.LocalId;
	public string? TargetMethodDisplayName { get; } = working.Method.FullName;

	public int InstructionCount {
		get {
			ensureAuthoringOpen();
			return snapshot.Instructions.Length;
		}
	}

	public TypeReference Import(Type type) {
		ensureAuthoringOpen();
		ArgumentNullException.ThrowIfNull(type);
		return working.Method.Module.ImportReference(type);
	}

	public TypeReference Import(TypeReference type) {
		ensureAuthoringOpen();
		ArgumentNullException.ThrowIfNull(type);
		return working.Method.Module.ImportReference(type);
	}

	public MethodReference Import(MethodBase method) {
		ensureAuthoringOpen();
		ArgumentNullException.ThrowIfNull(method);
		return working.Method.Module.ImportReference(method);
	}

	public MethodReference Import(MethodReference method) {
		ensureAuthoringOpen();
		ArgumentNullException.ThrowIfNull(method);
		return working.Method.Module.ImportReference(method);
	}

	public FieldReference Import(FieldInfo field) {
		ensureAuthoringOpen();
		ArgumentNullException.ThrowIfNull(field);
		return working.Method.Module.ImportReference(field);
	}

	public FieldReference Import(FieldReference field) {
		ensureAuthoringOpen();
		ArgumentNullException.ThrowIfNull(field);
		return working.Method.Module.ImportReference(field);
	}

	public IlLabel DefineLabel() {
		ensureAuthoringOpen();
		int labelId = checked(++nextLabelId);
		labels.Add(labelId, new IlLabelState());
		knownLabels.Add(labelId);
		return new IlLabel(transactionId, labelId);
	}

	public void EmitAtBoundary(int boundary, IlEmitAction emit) {
		ensureAuthoringOpen();
		ArgumentNullException.ThrowIfNull(emit);
		if ((uint)boundary > (uint)snapshot.Instructions.Length)
			throw new ArgumentOutOfRangeException(nameof(boundary));

		IlFragmentBuilder builder = new(transactionId, knownLabels);
		IlFragment fragment;
		try {
			IlEmitter emitter = new(builder);
			emit(emitter);
			fragment = builder.Finish();
		} finally {
			builder.DropStrongReferences();
		}

		foreach (int labelId in fragment.MarkedLabels)
			if (labels[labelId].Marked)
				throw new IlPipelineException($"IL label {labelId} is marked more than once");
		foreach (int labelId in fragment.ReferencedLabels)
			labels[labelId].Referenced = true;
		foreach (int labelId in fragment.MarkedLabels)
			labels[labelId].Marked = true;

		insertions.Add(new IlInsertionEdit(boundary, checked(nextEditSequence++), fragment));
	}

	public IlMatches MatchAll(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) {
		ensureAuthoringOpen();
		validatePattern(pattern, nameof(pattern));
		validateProvenanceConstraint(provenance, nameof(provenance));
		List<IlRange> matches = new();
		int lastStart = snapshot.Instructions.Length - pattern.Length;
		for (int start = 0; start <= lastStart; start++)
			if (matchesAt(start, pattern, provenance))
				matches.Add(new IlRange(start, start + pattern.Length));
		IlPatternSearchInfo info = new(StartInstructionBoundary: 0, Direction: IlSearchDirection.Forward, IlPatternDisplay.FormatPattern(pattern, provenance));
		return new IlMatches(this, matches.ToArray(), info);
	}

	public IlMatch MatchNext(int startBoundary, ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) {
		ensureAuthoringOpen();
		validatePattern(pattern, nameof(pattern));
		validateProvenanceConstraint(provenance, nameof(provenance));
		if ((uint)startBoundary > (uint)snapshot.Instructions.Length)
			throw new ArgumentOutOfRangeException(nameof(startBoundary));
		int lastStart = snapshot.Instructions.Length - pattern.Length;
		for (int start = startBoundary; start <= lastStart; start++)
			if (matchesAt(start, pattern, provenance))
				return new IlMatch(this, new IlRange(start, start + pattern.Length));
		IlPatternSearchInfo info = new(StartInstructionBoundary: startBoundary, Direction: IlSearchDirection.Forward, IlPatternDisplay.FormatPattern(pattern, provenance));
		throw IlMatchException.ExpectedAny(in info);
	}

	public IlMatch MatchPrev(int endBoundary, ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) {
		ensureAuthoringOpen();
		validatePattern(pattern, nameof(pattern));
		validateProvenanceConstraint(provenance, nameof(provenance));
		if ((uint)endBoundary > (uint)snapshot.Instructions.Length)
			throw new ArgumentOutOfRangeException(nameof(endBoundary));
		for (int start = endBoundary - pattern.Length; start >= 0; start--)
			if (matchesAt(start, pattern, provenance))
				return new IlMatch(this, new IlRange(start, start + pattern.Length));
		IlPatternSearchInfo info = new(StartInstructionBoundary: endBoundary, Direction: IlSearchDirection.Backward, IlPatternDisplay.FormatPattern(pattern, provenance));
		throw IlMatchException.ExpectedAny(in info);
	}

	public IlProvenance GetProvenance(int instrIdx) {
		ensureAuthoringOpen();
		if ((uint)instrIdx >= (uint)snapshot.Instructions.Length)
			throw new ArgumentOutOfRangeException(nameof(instrIdx));
		return new IlProvenance(snapshot.Provenance[instrIdx].OwnerId);
	}

	public void Commit() {
		ensureCanCommit();
		foreach ((int labelId, IlLabelState state) in labels)
			if (state.Referenced && !state.Marked)
				throw new IlPipelineException($"referenced IL label {labelId} was never marked");

		Dictionary<int, List<IlInsertionEdit>> byBoundary = new();
		foreach (IlInsertionEdit ins in insertions) {
			if (!byBoundary.TryGetValue(ins.Boundary, out List<IlInsertionEdit>? list)) {
				list = new List<IlInsertionEdit>();
				byBoundary.Add(ins.Boundary, list);
			}
			list.Add(ins);
		}
		foreach (List<IlInsertionEdit> list in byBoundary.Values)
			list.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));

		List<IlOutputNode> output = new();
		for (int boundary = 0; boundary <= snapshot.Instructions.Length; boundary++) {
			if (byBoundary.TryGetValue(boundary, out List<IlInsertionEdit>? edits))
				foreach (IlInsertionEdit edit in edits)
					appendFragment(output, edit.Fragment);
			if (boundary < snapshot.Instructions.Length) {
				Instruction instr = snapshot.Instructions[boundary];
				output.Add(new IlOutputInstructionNode(instr, snapshot.Provenance[boundary], null));
			}
		}

		Dictionary<int, Instruction> labelTargets = resolveLabelTargets(output);
		List<Instruction> finalInstrs = new();
		Dictionary<Instruction, InternalIlProvenance> finalProvenance = new(InstructionReferenceComparer.Instance);
		foreach (IlOutputNode node in output) {
			if (node is not IlOutputInstructionNode instrNode)
				continue;
			if (instrNode.PendingSpec is IlInstructionSpec spec)
				resolvePendingOperand(instrNode.Instruction, spec, labelTargets);
			finalInstrs.Add(instrNode.Instruction);
			finalProvenance.Add(instrNode.Instruction, instrNode.Provenance);
		}

		working.ReplaceInstructions(finalInstrs, finalProvenance);
		committed = true;
	}

	private void appendFragment(List<IlOutputNode> output, IlFragment fragment) {
		foreach (IlFragmentNode node in fragment.Nodes) {
			switch (node) {
			case IlFragmentLabelNode label:
				output.Add(new IlOutputLabelNode(label.LabelId));
				break;
			case IlFragmentInstructionNode instrs:
				if (instrs.Spec.Kind == IlInstructionSpecKind.ManagedDelegate) {
					appendManagedDelegate(output, instrs.Spec);
				} else {
					Instruction lowered = lowerInstr(instrs.Spec);
					output.Add(new IlOutputInstructionNode(lowered, currProvenance, instrs.Spec));
				}
				break;
			default:
				throw new IlPipelineException($"unknown IL fragment node type '{node.GetType()}'");
			}
		}
	}

	private void appendManagedDelegate(List<IlOutputNode> output, IlInstructionSpec spec) {
		if (managedDelegateLowerer is null)
			throw new InternalStateException($"IL manipulator '{currProvenance.OwnerId}::{currProvenance.LocalId}' emitted a managed delegate but no managed-delegate lowerer was configured");

		Delegate callback = spec.Operand as Delegate ?? throw new InternalStateException("managed-delegate instruction specification has no delegate operand");
		IlManagedDelegateLoweringRequest req = new(
			working.Method,
			callback,
			currProvenance.OwnerId ?? throw new InternalStateException("manipulator provenance has no owner ID"),
			currProvenance.LocalId ?? throw new InternalStateException("manipulator provenance has no local ID")
		);

		IlManagedDelegateLowering lowering;
		try {
			lowering = managedDelegateLowerer.Lower(in req) ?? throw new InternalStateException("managed-delegate lowerer returned null");
		} catch (Exception ex) {
			if (ExceptionPolicy.IsInternalState(ex))
				throw;
			throw new InternalStateException($"managed-delegate lowerer for '{req.OwnerId}::{req.LocalId}' threw", ex);
		}

		IDisposable? retention = lowering.TakeRetention();
		if (retention is not null)
			retentions.Add(retention);

		Instruction[] instrs = lowering.TakeInstructions();
		lowering.DropStrongReferences();
		foreach (Instruction instr in instrs) {
			if (!loweredManagedInstrs.Add(instr))
				throw new InternalStateException("managed-delegate lowerer returned the same instruction more than once");
			if (instr.Previous is not null || instr.Next is not null || working.Body.Instructions.Contains(instr))
				throw new InternalStateException("managed-delegate lowerer returned an instruction already attached to a Cecil instruction collection");
			output.Add(new IlOutputInstructionNode(instr, currProvenance, null));
		}

	}

	private Dictionary<int, Instruction> resolveLabelTargets(List<IlOutputNode> output) {
		Dictionary<int, Instruction> targets = new();
		Instruction? nextInstr = null;
		List<int> trailingLabels = new();
		for (int i = output.Count - 1; i >= 0; i--) {
			switch (output[i]) {
			case IlOutputInstructionNode instr:
				nextInstr = instr.Instruction;
				break;
			case IlOutputLabelNode label when nextInstr is not null:
				targets.Add(label.LabelId, nextInstr);
				break;
			case IlOutputLabelNode label:
				trailingLabels.Add(label.LabelId);
				break;
			}
		}

		if (trailingLabels.Count != 0) {
			var anchor = Instruction.Create(OpCodes.Nop);
			output.Add(new IlOutputInstructionNode(anchor, currProvenance, null));
			foreach (int labelId in trailingLabels)
				targets.Add(labelId, anchor);
		}
		return targets;
	}

	private Instruction lowerInstr(IlInstructionSpec spec) {
		return spec.Kind switch {
			IlInstructionSpecKind.Raw => createRawInstr(spec.OpCode, spec.Operand),

			IlInstructionSpecKind.LdcI4 => createLdcI4(spec.Int),

			IlInstructionSpecKind.Ldarg => createLdarg(spec.Int),
			IlInstructionSpecKind.Ldarga => createLongOrShortArgInstr(spec.Int, OpCodes.Ldarga_S, OpCodes.Ldarga),
			IlInstructionSpecKind.Starg => createLongOrShortArgInstr(spec.Int, OpCodes.Starg_S, OpCodes.Starg),

			IlInstructionSpecKind.Ldloc => createLdloc(spec.Int),
			IlInstructionSpecKind.Ldloca => createLdloca(spec.Int),
			IlInstructionSpecKind.Stloc => createStloc(spec.Int),

			IlInstructionSpecKind.Branch => Instruction.Create(spec.OpCode, Instruction.Create(OpCodes.Nop)),
			IlInstructionSpecKind.Switch => Instruction.Create(OpCodes.Switch, Array.Empty<Instruction>()),

			IlInstructionSpecKind.ManagedDelegate => throw new InternalStateException("managed delegates should be lowered as fragments"),
			_ => throw new IlPipelineException($"unsupported IL instruction specification kind '{spec.Kind}'"),
		};
	}

	private static void resolvePendingOperand(Instruction instr, IlInstructionSpec spec, Dictionary<int, Instruction> labelTargets) {
		switch (spec.Kind) {
		case IlInstructionSpecKind.Branch:
			instr.Operand = labelTargets[spec.Labels![0].LabelId];
			break;
		case IlInstructionSpecKind.Switch:
			instr.Operand = spec.Labels!.Select(label => labelTargets[label.LabelId]).ToArray();
			break;
		}
	}

	private Instruction createRawInstr(OpCode opCode, object? operand) {
		validateOperandShape(opCode, operand);
		if (operand is null)
			return Instruction.Create(opCode);

		ModuleDefinition module = working.Method.Module;
		return operand switch {
			sbyte v => Instruction.Create(opCode, v),
			byte v => Instruction.Create(opCode, v),
			int v => Instruction.Create(opCode, v),
			long v => Instruction.Create(opCode, v),
			float v => Instruction.Create(opCode, v),
			double v => Instruction.Create(opCode, v),
			string v => Instruction.Create(opCode, v),
			TypeReference v => Instruction.Create(opCode, module.ImportReference(v)),
			FieldReference v => Instruction.Create(opCode, module.ImportReference(v)),
			MethodReference v => Instruction.Create(opCode, module.ImportReference(v)),
			CallSite v => Instruction.Create(opCode, v),
			VariableDefinition v => Instruction.Create(opCode, v),
			ParameterDefinition v => Instruction.Create(opCode, v),
			_ => throw new IlPipelineException($"unsupported raw IL operand type '{operand.GetType()}'"),
		};
	}

	private static void validateOperandShape(OpCode opCode, object? operand) {
		switch (opCode.OperandType) {
		case OperandType.InlineNone:
			if (operand is not null)
				throw new IlPipelineException($"opcode '{opCode}' does not take an operand");
			return;
		case OperandType.InlineField:
			if (operand is not FieldReference)
				throw new IlPipelineException($"opcode '{opCode}' requires a field operand");
			return;
		case OperandType.InlineMethod:
			if (operand is not MethodReference and not CallSite)
				throw new IlPipelineException($"opcode '{opCode}' requires a method operand");
			return;
		case OperandType.InlineType:
			if (operand is not TypeReference)
				throw new IlPipelineException($"opcode '{opCode}' requires a type operand");
			return;
		case OperandType.InlineString:
			if (operand is not string)
				throw new IlPipelineException($"opcode '{opCode}' requires a string operand");
			return;
		case OperandType.InlineI:
			if (operand is not int)
				throw new IlPipelineException($"opcode '{opCode}' requires a 32-bit integer operand");
			return;
		case OperandType.ShortInlineI:
			if (operand is not sbyte and not byte)
				throw new IlPipelineException($"opcode '{opCode}' requires a short integer operand");
			return;
		case OperandType.InlineI8:
			if (operand is not long)
				throw new IlPipelineException($"opcode '{opCode}' requires a 64-bit integer operand");
			return;
		case OperandType.ShortInlineR:
			if (operand is not float)
				throw new IlPipelineException($"opcode '{opCode}' requires a float (f32) operand");
			return;
		case OperandType.InlineR:
			if (operand is not double)
				throw new IlPipelineException($"opcode '{opCode}' requires a double (f64) operand");
			return;
		case OperandType.InlineVar:
		case OperandType.ShortInlineVar:
			if (operand is not VariableDefinition and not ParameterDefinition)
				throw new IlPipelineException($"opcode '{opCode}' requires a variable or parameter operand");
			return;
		}
	}

	private static Instruction createLdcI4(int v) {
		return v switch {
			-1 => Instruction.Create(OpCodes.Ldc_I4_M1),
			0 => Instruction.Create(OpCodes.Ldc_I4_0),
			1 => Instruction.Create(OpCodes.Ldc_I4_1),
			2 => Instruction.Create(OpCodes.Ldc_I4_2),
			3 => Instruction.Create(OpCodes.Ldc_I4_3),
			4 => Instruction.Create(OpCodes.Ldc_I4_4),
			5 => Instruction.Create(OpCodes.Ldc_I4_5),
			6 => Instruction.Create(OpCodes.Ldc_I4_6),
			7 => Instruction.Create(OpCodes.Ldc_I4_7),
			8 => Instruction.Create(OpCodes.Ldc_I4_8),
			>= sbyte.MinValue and <= sbyte.MaxValue => Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)v),
			_ => Instruction.Create(OpCodes.Ldc_I4, v),
		};
	}

	private Instruction createLdarg(int idx) {
		int argumentCount = snapshot.Method.Parameters.Count + (snapshot.Method.HasThis ? 1 : 0);
		if ((uint)idx >= (uint)argumentCount)
			throw new IlPipelineException($"IL argument index {idx} is invalid for method '{snapshot.Method.FullName}'");
		return idx switch {
			0 => Instruction.Create(OpCodes.Ldarg_0),
			1 => Instruction.Create(OpCodes.Ldarg_1),
			2 => Instruction.Create(OpCodes.Ldarg_2),
			3 => Instruction.Create(OpCodes.Ldarg_3),
			_ => Instruction.Create(idx <= byte.MaxValue ? OpCodes.Ldarg_S : OpCodes.Ldarg, getParameterForIlArgument(idx)),
		};
	}

	private Instruction createLongOrShortArgInstr(int idx, OpCode @short, OpCode @long) {
		int argumentCount = snapshot.Method.Parameters.Count + (snapshot.Method.HasThis ? 1 : 0);
		if ((uint)idx >= (uint)argumentCount)
			throw new IlPipelineException($"IL argument index {idx} is invalid for method '{snapshot.Method.FullName}'");
		return Instruction.Create(idx <= byte.MaxValue ? @short : @long, getParameterForIlArgument(idx));
	}

	private ParameterDefinition getParameterForIlArgument(int idx) {
		int paramIdx = snapshot.Method.HasThis ? idx - 1 : idx;
		if ((uint)paramIdx >= (uint)snapshot.Method.Parameters.Count)
			throw new IlPipelineException($"IL argument index {idx} is invalid for method '{snapshot.Method.FullName}'");
		return snapshot.Method.Parameters[paramIdx];
	}

	private Instruction createLdloc(int idx) {
		if ((uint)idx >= (uint)working.Body.Variables.Count)
			throw new IlPipelineException($"IL local index {idx} is invalid for method '{snapshot.Method.FullName}'");
		return idx switch {
			0 => Instruction.Create(OpCodes.Ldloc_0),
			1 => Instruction.Create(OpCodes.Ldloc_1),
			2 => Instruction.Create(OpCodes.Ldloc_2),
			3 => Instruction.Create(OpCodes.Ldloc_3),
			_ => Instruction.Create(idx <= byte.MaxValue ? OpCodes.Ldloc_S : OpCodes.Ldloc, working.Body.Variables[idx]),
		};
	}

	private Instruction createLdloca(int idx) {
		if ((uint)idx >= (uint)working.Body.Variables.Count)
			throw new IlPipelineException($"IL local index {idx} is invalid for method '{snapshot.Method.FullName}'");
		return Instruction.Create(idx <= byte.MaxValue ? OpCodes.Ldloca_S : OpCodes.Ldloca, working.Body.Variables[idx]);
	}

	private Instruction createStloc(int idx) {
		if ((uint)idx >= (uint)working.Body.Variables.Count)
			throw new IlPipelineException($"IL local index {idx} is invalid for method '{snapshot.Method.FullName}'");
		return idx switch {
			0 => Instruction.Create(OpCodes.Stloc_0),
			1 => Instruction.Create(OpCodes.Stloc_1),
			2 => Instruction.Create(OpCodes.Stloc_2),
			3 => Instruction.Create(OpCodes.Stloc_3),
			_ => Instruction.Create(idx <= byte.MaxValue ? OpCodes.Stloc_S : OpCodes.Stloc, working.Body.Variables[idx]),
		};
	}

	private bool matchesAt(int start, ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) {
		if (!matchesProvenance(start, pattern.Length, provenance))
			return false;
		for (int offset = 0; offset < pattern.Length; offset++) {
			int idx = start + offset;
			if (!matchesElement(snapshot.Instructions[idx], pattern[offset]))
				return false;
		}
		return true;
	}

	private bool matchesProvenance(int start, int length, IlPatternProvenanceConstraint provenance) {
		return provenance.Kind switch {
			IlPatternProvenanceConstraint.ConstraintKind.Any => true,
			IlPatternProvenanceConstraint.ConstraintKind.AllFromOwner => allFromOwner(start, length, provenance.OwnerId),
			IlPatternProvenanceConstraint.ConstraintKind.AllUnknown => allFromOwner(start, length, null),
			IlPatternProvenanceConstraint.ConstraintKind.AllUniform => allUniformKnown(start, length),
			_ => throw new InternalStateException("thought we validated this earlier"),
		};
	}

	private bool allFromOwner(int start, int length, string? ownerId) {
		for (int offset = 0; offset < length; offset++)
			if (!StringComparer.Ordinal.Equals(snapshot.Provenance[start + offset].OwnerId, ownerId))
				return false;
		return true;
	}

	private bool allUniformKnown(int start, int length) {
		string? first = snapshot.Provenance[start].OwnerId;
		if (first is null)
			return false;
		for (int offset = 1; offset < length; offset++)
			if (!StringComparer.Ordinal.Equals(snapshot.Provenance[start + offset].OwnerId, first))
				return false;
		return true;
	}

	private bool matchesElement(Instruction instr, IlPatternElement elem) {
		return elem.Kind switch {
			IlPatternElementKind.UninitializedValue => throw new InternalStateException("thought we validated this earlier"),
			IlPatternElementKind.Any => true,

			IlPatternElementKind.OpCode => instr.OpCode == elem.OpCode,

			IlPatternElementKind.LdcI4 => tryGetLdcI4Value(instr, out int v) && v == elem.Int,
			IlPatternElementKind.LdcI8 => instr.OpCode.Code == Code.Ldc_I8 && instr.Operand is long lv && lv == elem.Long,
			IlPatternElementKind.LdcR4 => instr.OpCode.Code == Code.Ldc_R4 && instr.Operand is float fv && fv == elem.Float,
			IlPatternElementKind.LdcR8 => instr.OpCode.Code == Code.Ldc_R8 && instr.Operand is double dv && dv == elem.Double,

			IlPatternElementKind.Ldarg => tryGetLdargIdx(instr, out int idx) && idx == elem.Int,
			IlPatternElementKind.Ldarga => tryGetLdargaOrStargIdx(instr, ldarga: true, out int idx) && idx == elem.Int,
			IlPatternElementKind.Starg => tryGetLdargaOrStargIdx(instr, ldarga: false, out int idx) && idx == elem.Int,

			IlPatternElementKind.Ldloc => tryGetLdlocOrStlocIdx(instr, ldloc: true, out int idx) && idx == elem.Int,
			IlPatternElementKind.Ldloca => tryGetLdlocaIdx(instr, out int idx) && idx == elem.Int,
			IlPatternElementKind.Stloc => tryGetLdlocOrStlocIdx(instr, ldloc: false, out int idx) && idx == elem.Int,

			IlPatternElementKind.CecilField =>
				instr.OpCode.Code == elem.OpCode.Code && instr.Operand is FieldReference f &&
				CecilMemberIdentity.SameField(f, elem.CecilField ?? throw new InternalStateException("IlPatternElement of kind CecilField is missing its CecilField value")),
			IlPatternElementKind.ReflectionField =>
				instr.OpCode.Code == elem.OpCode.Code && instr.Operand is FieldReference f &&
				CecilMemberIdentity.SameField(f, elem.ReflectionField ?? throw new InternalStateException("IlPatternElement of kind ReflectionField is missing its ReflectionField value")),

			IlPatternElementKind.CecilMethod =>
				instr.OpCode.Code == elem.OpCode.Code && instr.Operand is MethodReference m &&
				CecilMemberIdentity.SameMethod(m, elem.CecilMethod ?? throw new InternalStateException("IlPatternElement of kind CecilMethod is missing its CecilMethod value")),

			IlPatternElementKind.ReflectionMethod =>
				instr.OpCode.Code == elem.OpCode.Code && instr.Operand is MethodReference m &&
				CecilMemberIdentity.SameMethod(m, elem.ReflectionMethod ?? throw new InternalStateException("IlPatternElement of kind ReflectionMethod is missing its ReflectionMethod value")),

			_ => throw new InternalStateException($"unknown IlPatternElementKind '{elem.Kind}'"),
		};
	}

	private static bool tryGetLdcI4Value(Instruction instr, out int v) {
		switch (instr.OpCode.Code) {
		case Code.Ldc_I4_M1:
			v = -1;
			return true;
		case Code.Ldc_I4_0:
			v = 0;
			return true;
		case Code.Ldc_I4_1:
			v = 1;
			return true;
		case Code.Ldc_I4_2:
			v = 2;
			return true;
		case Code.Ldc_I4_3:
			v = 3;
			return true;
		case Code.Ldc_I4_4:
			v = 4;
			return true;
		case Code.Ldc_I4_5:
			v = 5;
			return true;
		case Code.Ldc_I4_6:
			v = 6;
			return true;
		case Code.Ldc_I4_7:
			v = 7;
			return true;
		case Code.Ldc_I4_8:
			v = 8;
			return true;
		case Code.Ldc_I4_S when instr.Operand is sbyte sv:
			v = sv;
			return true;
		case Code.Ldc_I4 when instr.Operand is int iv:
			v = iv;
			return true;
		default:
			v = default;
			return false;
		}
	}

	private bool tryGetLdargIdx(Instruction instr, out int idx) {
		idx = instr.OpCode.Code switch {
			Code.Ldarg_0 => 0,
			Code.Ldarg_1 => 1,
			Code.Ldarg_2 => 2,
			Code.Ldarg_3 => 3,
			_ => -1,
		};
		if (idx >= 0)
			return true;
		if (instr.OpCode.Code is not Code.Ldarg and not Code.Ldarg_S || instr.Operand is not ParameterDefinition param)
			return false;
		idx = param.Index + (snapshot.Method.HasThis ? 1 : 0);
		return true;
	}

	private bool tryGetLdargaOrStargIdx(Instruction instr, bool ldarga, out int idx) {
		Code code = instr.OpCode.Code;
		bool opcodeMatches = ldarga ? code is Code.Ldarga or Code.Ldarga_S : code is Code.Starg or Code.Starg_S;
		if (!opcodeMatches || instr.Operand is not ParameterDefinition param) {
			idx = -1;
			return false;
		}
		idx = param.Index + (snapshot.Method.HasThis ? 1 : 0);
		return true;
	}

	private static bool tryGetLdlocOrStlocIdx(Instruction instr, bool ldloc, out int index) {
		Code code = instr.OpCode.Code;
		index = code switch {
			Code.Ldloc_0 when ldloc => 0,
			Code.Ldloc_1 when ldloc => 1,
			Code.Ldloc_2 when ldloc => 2,
			Code.Ldloc_3 when ldloc => 3,
			Code.Stloc_0 when !ldloc => 0,
			Code.Stloc_1 when !ldloc => 1,
			Code.Stloc_2 when !ldloc => 2,
			Code.Stloc_3 when !ldloc => 3,
			_ => -1,
		};
		if (index >= 0)
			return true;
		bool opcodeMatches = ldloc ? code is Code.Ldloc or Code.Ldloc_S : code is Code.Stloc or Code.Stloc_S;
		if (!opcodeMatches || instr.Operand is not VariableDefinition @var)
			return false;
		index = @var.Index;
		return true;
	}

	private static bool tryGetLdlocaIdx(Instruction instr, out int index) {
		if (instr.OpCode.Code is not Code.Ldloca and not Code.Ldloca_S || instr.Operand is not VariableDefinition @var) {
			index = -1;
			return false;
		}
		index = @var.Index;
		return true;
	}

	private static void validatePattern(ReadOnlySpan<IlPatternElement> pattern, string paramName) {
		if (pattern.IsEmpty)
			throw new ArgumentException("IL match pattern cannot be empty", paramName);
		for (int i = 0; i < pattern.Length; i++)
			if (!pattern[i].IsValid)
				throw new ArgumentException($"IL pattern element at index {i} is invalid; use MatchIl.Any if you intended to do a single-instruction wildcard match", paramName);
	}

	private static void validateProvenanceConstraint(IlPatternProvenanceConstraint provenance, string paramName) {
		switch (provenance.Kind) {
		case IlPatternProvenanceConstraint.ConstraintKind.Any:
		case IlPatternProvenanceConstraint.ConstraintKind.AllUnknown:
		case IlPatternProvenanceConstraint.ConstraintKind.AllUniform:
			break;
		case IlPatternProvenanceConstraint.ConstraintKind.AllFromOwner:
			if (provenance.OwnerId is null)
				throw new InternalStateException("IlMatchProvenanceConstraint has AllFromOwner ConstraintKind but null OwnerId");
			break;
		case IlPatternProvenanceConstraint.ConstraintKind.UninitializedValue:
			throw new ArgumentException("invalid/uninitialized IlMatchProvenanceConstraint value", paramName);
		default:
			throw new InternalStateException($"unexpected IlMatchProvenanceConstraint.ConstraintKind value '{provenance.Kind}'");
		}
	}

	public void CloseAuthoring() {
		authoringClosed = true;
	}

	public void DropStrongReferences() {
		dropped = true;
		managedDelegateLowerer = null;
		loweredManagedInstrs.Clear();
		labels.Clear();
		knownLabels.Clear();
		foreach (IlInsertionEdit insertion in insertions)
			insertion.Fragment.DropStrongReferences();
		insertions.Clear();
		snapshotBacking?.DropStrongReferences();
		snapshotBacking = null;
		workingBacking = null;
		retentionsBacking = null;
	}

	private void ensureAuthoringOpen() {
		ensureNotDropped();
		if (committed)
			throw new InternalStateException("IlTransactionCore accessed after commit, the reference to this should've been dropped higher up");
		if (authoringClosed)
			throw new IlTransactionExpiredException(OwnerId, LocalId, TargetMethodDisplayName);
	}

	private void ensureCanCommit() {
		ensureNotDropped();
		if (committed)
			throw new InternalStateException("IL manipulation transaction was committed more than once");
	}

	private void ensureNotDropped() {
		if (dropped)
			throw new InternalStateException("IL transaction was used after its strong references were dropped");
	}
}
