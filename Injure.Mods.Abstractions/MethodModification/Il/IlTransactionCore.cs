// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// One manipulator's transaction over a method body.
/// </summary>
/// <remarks>
/// <para>
/// A transaction reads from an <see cref="IlSnapshot"/> taken when it opened and writes nothing
/// until <see cref="Commit"/> runs. Matching, boundary indices, and provenance all describe the
/// snapshot, so emitting never disturbs a boundary the manipulator already holds, and the working
/// body stays untouched if the manipulator throws.
/// </para>
/// <para>
/// Emitting at a boundary inserts the new instructions <b>before</b> that boundary's original
/// anchor, which is reattached after them at commit. An existing branch or exception region that
/// named the anchor therefore continues to target the original instruction rather than the inserted
/// fragment, and the fragment is reached only by fall-through. The same rule decides where an
/// insertion lands relative to a protected region: at a try-start boundary it falls outside the
/// region, and at a try-end or handler-end boundary it falls inside.
/// </para>
/// <para>
/// Anchors are stable. Every anchor in the snapshot exists in the committed body and still names the
/// same original instruction, which is why exception regions survive edits with no adjustment.
/// Newly inserted instructions receive freshly allocated anchors.
/// </para>
/// <para>
/// Not thread-safe. Scoped to a single manipulator invocation.
/// </para>
/// </remarks>
internal sealed class IlTransactionCore {
	private sealed class LabelState {
		public bool Referenced;
		public bool Marked;
	}

	private readonly record struct Insertion(int Boundary, int Sequence, IlFragment Fragment);

	private static ulong nextTransactionId;
	private readonly IlMethodBody working;
	private readonly InternalIlProvenance provenance;
	private readonly IlSnapshot snapshot;
	private readonly IlFormatContext formatContext;
	private readonly ulong transactionId;
	private readonly Dictionary<int, LabelState> labels = new();
	private readonly List<Insertion> insertions = new();
	private int nextLabelId;
	private int nextInsertionSequence;
	private bool authoringOpen = true;
	private bool committed;

	public string OwnerId { get; }
	public string LocalId { get; }
	public string TargetMethodDisplayName => working.Method.ToString();
	public IlMethodRef TargetMethod {
		get {
			EnsureAuthoringOpen();
			return working.Method;
		}
	}

	/// <summary>
	/// The number of instructions in the snapshot, and therefore the highest valid boundary index.
	/// </summary>
	public int InstructionCount {
		get {
			EnsureAuthoringOpen();
			return snapshot.Instructions.Length;
		}
	}

	/// <summary>
	/// Whether this transaction has buffered at least one edit.
	/// </summary>
	/// <remarks>
	/// The pipeline uses this to skip committing a manipulator that did nothing.
	/// </remarks>
	public bool HasPendingEdits => insertions.Count > 0;

	public IlTransactionCore(IlMethodBody working, string ownerId, string localId) {
		InternalStateException.ThrowIfNull(working);
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		InternalStateException.ThrowIfInvalidLocalId(localId);
		this.working = working;
		provenance = new InternalIlProvenance(ownerId, localId);
		snapshot = new IlSnapshot(working);
		formatContext = new IlFormatContext(snapshot);
		transactionId = Interlocked.Increment(ref nextTransactionId);
		if (transactionId == 0)
			throw new InternalStateException("transaction ID counter wrapped to zero");
		OwnerId = ownerId;
		LocalId = localId;
	}

	public IlLabel DefineLabel() {
		EnsureAuthoringOpen();
		int id = checked(++nextLabelId);
		labels.Add(id, new LabelState());
		return new IlLabel(transactionId, id);
	}

	/// <summary>
	/// Emits a fragment immediately before the given snapshot boundary.
	/// </summary>
	/// <remarks>
	/// The fragment is buffered, not applied. Several fragments emitted at the same boundary are
	/// appended in the order the emit calls were made. See the type-level remarks for how insertion
	/// interacts with existing branches and protected regions.
	/// </remarks>
	public void EmitAtBoundary(int boundary, IlEmitAction emit) {
		EnsureAuthoringOpen();
		ArgumentNullException.ThrowIfNull(emit);
		if ((uint)boundary > (uint)snapshot.Instructions.Length)
			throw new ArgumentOutOfRangeException(nameof(boundary));

		IlFragmentBuilder builder = new(transactionId, labels.Keys.ToHashSet());
		emit(new IlEmitter(builder));
		IlFragment fragment = builder.Finish();

		foreach (int labelId in fragment.MarkedLabels)
			if (labels[labelId].Marked)
				throw new IlPipelineException($"IL label {labelId} is marked more than once");
		foreach (int labelId in fragment.ReferencedLabels)
			labels[labelId].Referenced = true;
		foreach (int labelId in fragment.MarkedLabels)
			labels[labelId].Marked = true;

		insertions.Add(new Insertion(boundary, checked(nextInsertionSequence++), fragment));
	}

	/// <summary>
	/// Finds every non-overlapping occurrence of a pattern, scanning forward from the start of the
	/// snapshot.
	/// </summary>
	/// <remarks>
	/// Emission does not invalidate matches.
	/// </remarks>
	public IlMatches MatchAll(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenanceConstraint) {
		EnsureAuthoringOpen();
		validatePattern(pattern, nameof(pattern));
		validateProvenanceConstraint(provenanceConstraint, nameof(provenanceConstraint));
		List<IlRange> matches = new();
		int lastStart = snapshot.Instructions.Length - pattern.Length;
		for (int start = 0; start <= lastStart; ) {
			if (matchesAt(start, pattern, provenanceConstraint)) {
				matches.Add(new IlRange(start, start + pattern.Length));
				start += pattern.Length;
			} else {
				start++;
			}
		}
		IlPatternSearchInfo info = new(0, IlSearchDirection.Forward, pattern, provenanceConstraint, formatContext);
		return new IlMatches(this, matches.ToArray(), info);
	}

	/// <summary>
	/// Finds the first occurrence of a pattern beginning at or after a boundary.
	/// </summary>
	public IlMatch MatchNext(int startBoundary, ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenanceConstraint) {
		EnsureAuthoringOpen();
		validatePattern(pattern, nameof(pattern));
		validateProvenanceConstraint(provenanceConstraint, nameof(provenanceConstraint));
		if ((uint)startBoundary > (uint)snapshot.Instructions.Length)
			throw new ArgumentOutOfRangeException(nameof(startBoundary));
		int lastStart = snapshot.Instructions.Length - pattern.Length;
		for (int start = startBoundary; start <= lastStart; start++)
			if (matchesAt(start, pattern, provenanceConstraint))
				return new IlMatch(this, new IlRange(start, start + pattern.Length));
		IlPatternSearchInfo info = new(startBoundary, IlSearchDirection.Forward, pattern, provenanceConstraint, formatContext);
		throw IlMatchException.ExpectedAny(in info);
	}

	/// <summary>
	/// Finds the last occurrence of a pattern ending at or before a boundary.
	/// </summary>
	public IlMatch MatchPrev(int endBoundary, ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenanceConstraint) {
		EnsureAuthoringOpen();
		validatePattern(pattern, nameof(pattern));
		validateProvenanceConstraint(provenanceConstraint, nameof(provenanceConstraint));
		if ((uint)endBoundary > (uint)snapshot.Instructions.Length)
			throw new ArgumentOutOfRangeException(nameof(endBoundary));
		for (int start = endBoundary - pattern.Length; start >= 0; start--)
			if (matchesAt(start, pattern, provenanceConstraint))
				return new IlMatch(this, new IlRange(start, start + pattern.Length));
		IlPatternSearchInfo info = new(endBoundary, IlSearchDirection.Backward, pattern, provenanceConstraint, formatContext);
		throw IlMatchException.ExpectedAny(in info);
	}

	public InternalIlProvenance GetProvenance(int instrIdx) {
		EnsureAuthoringOpen();
		if ((uint)instrIdx >= (uint)snapshot.Instructions.Length)
			throw new ArgumentOutOfRangeException(nameof(instrIdx));
		return snapshot.Instructions[instrIdx].Provenance;
	}

	public void EnsureAuthoringOpen() {
		if (!authoringOpen)
			throw new InternalStateException("IlTransactionCore authoring somehow accessed despite transaction having ended");
	}

	/// <summary>
	/// Closes authoring and discards every buffered edit.
	/// </summary>
	/// <remarks>
	/// Discarding is free: nothing has been written to the working body, so this only prevents further
	/// emits if the transaction is abandoned.
	/// </remarks>
	public void Abort() => authoringOpen = false;

	/// <summary>
	/// Applies every buffered edit to the working body as one atomic replacement.
	/// </summary>
	/// <remarks>
	/// Edit application is all-or-nothing; if something fails, the working body is left unchanged.
	/// </remarks>
	public void Commit() {
		EnsureAuthoringOpen();
		authoringOpen = false;
		if (committed)
			throw new InternalStateException("transaction was already committed");

		foreach ((int labelId, LabelState state) in labels)
			if (state.Referenced && !state.Marked)
				throw new IlPipelineException($"referenced IL label {labelId} was never marked");

		Dictionary<int, List<Insertion>> insertionsByBoundary = new();
		foreach (Insertion insertion in insertions) {
			if (!insertionsByBoundary.TryGetValue(insertion.Boundary, out List<Insertion>? list)) {
				list = new List<Insertion>();
				insertionsByBoundary.Add(insertion.Boundary, list);
			}
			list.Add(insertion);
		}
		foreach (List<Insertion> list in insertionsByBoundary.Values)
			list.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

		List<IlInstruction> instrs = new();
		List<IlAnchorId?> anchors = [null]; // TODO: maybe rethink the new() vs [] style honestly
		Dictionary<int, int> labelBoundaries = new();
		List<(int Index, IlInstructionSpec Spec)> pendingOperands = new();

		for (int boundary = 0; boundary <= snapshot.Instructions.Length; boundary++) {
			if (insertionsByBoundary.TryGetValue(boundary, out List<Insertion>? edits))
				foreach (Insertion insertion in edits)
					appendFragment(insertion.Fragment, instrs, anchors, labelBoundaries, pendingOperands);

			if (boundary < snapshot.Instructions.Length) {
				setAnchor(anchors, instrs.Count, snapshot.Anchors[boundary]);
				instrs.Add(snapshot.Instructions[boundary]);
				anchors.Add(null);
			}
		}
		setAnchor(anchors, instrs.Count, snapshot.Anchors[^1]);

		List<IlAnchorId> finalAnchors = new(anchors.Count);
		foreach (IlAnchorId? anchor in anchors)
			finalAnchors.Add(anchor ?? working.AllocateAnchorId());

		Dictionary<int, IlAnchorId> labelTargets = [];
		foreach ((int labelId, int boundary) in labelBoundaries)
			labelTargets.Add(labelId, finalAnchors[boundary]);

		foreach ((int index, IlInstructionSpec spec) in pendingOperands) {
			IlLabel[] specLabels = spec.Labels ??
				throw new InternalStateException("pending branch/switch instruction has no labels");
			IlOperand operand;
			if (spec.OpCode == ILOpCode.Switch) {
				ImmutableArray<IlAnchorId>.Builder targets = ImmutableArray.CreateBuilder<IlAnchorId>(specLabels.Length);
				foreach (IlLabel label in specLabels)
					targets.Add(getLabelTarget(labelTargets, label.LabelId));
				operand = new IlSwitchOperand(targets.MoveToImmutable());
			} else {
				if (specLabels.Length != 1)
					throw new InternalStateException($"branch instruction {spec.OpCode} has {specLabels.Length} labels");
				operand = new IlBranchOperand(getLabelTarget(labelTargets, specLabels[0].LabelId));
			}
			instrs[index] = instrs[index] with { Operand = operand };
		}

		validateIndices(instrs);
		working.ReplaceInstructions(instrs, finalAnchors);
		committed = true;
	}

	private void appendFragment(
		IlFragment fragment,
		List<IlInstruction> instrs,
		List<IlAnchorId?> anchors,
		Dictionary<int, int> labelBoundaries,
		List<(int Index, IlInstructionSpec Spec)> pendingOperands
	) {
		foreach (IlFragmentNode node in fragment.Nodes)
			switch (node) {
			case IlFragmentLabelNode label:
				if (!labelBoundaries.TryAdd(label.LabelId, instrs.Count))
					throw new IlPipelineException($"IL label {label.LabelId} is marked more than once");
				break;
			case IlFragmentInstructionNode instrNode:
				IlInstructionSpec spec = instrNode.Spec;
				if (anchors[instrs.Count] is null)
					anchors[instrs.Count] = working.AllocateAnchorId();
				int index = instrs.Count;
				IlOperand operand = spec.Labels is null ? spec.Operand : IlNoneOperand.Instance;
				instrs.Add(new IlInstruction(
					working.AllocateInstructionId(),
					spec.OpCode,
					operand,
					Prefixes: null,
					IlInstruction.NoOriginalOffset,
					provenance
				));
				anchors.Add(null);
				if (spec.Labels is not null)
					pendingOperands.Add((index, spec));
				break;
			default:
				throw new InternalStateException($"unknown IL fragment node type '{node.GetType()}'");
			}
	}

	private bool matchesAt(int start, ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenanceConstraint) {
		for (int offset = 0; offset < pattern.Length; offset++) {
			IlInstruction instr = snapshot.Instructions[start + offset];
			IlPatternElement element = pattern[offset];
			bool instrMatches = element.Kind switch {
				IlPatternElement.PatternKind.Any => true,
				IlPatternElement.PatternKind.OpCode => instr.OpCode == element.OpCodeValue,
				IlPatternElement.PatternKind.Instruction => instr.OpCode == element.OpCodeValue && IlReferenceMatching.OperandEquals(instr.Operand, element.Operand),
				_ => throw new InternalStateException($"unexpected pattern kind '{element.Kind}' after validation"),
			};
			if (!instrMatches)
				return false;
		}

		ReadOnlySpan<IlInstruction> matched = snapshot.Instructions.AsSpan(start, pattern.Length);
		return provenanceConstraint.Kind switch {
			IlPatternProvenanceConstraint.ConstraintKind.Any => true,
			IlPatternProvenanceConstraint.ConstraintKind.AllUnknown => allFromOwner(matched, 0),
			IlPatternProvenanceConstraint.ConstraintKind.AllFromOwner => allFromOwner(matched, IlProvenanceInterning.Intern(provenanceConstraint.OwnerId)),
			IlPatternProvenanceConstraint.ConstraintKind.AllUniform => allUniform(matched),
			_ => throw new InternalStateException($"unexpected provenance constraint kind '{provenanceConstraint.Kind}' after validation"),
		};
	}

	private void validateIndices(List<IlInstruction> instrs) {
		int argumentCount = working.Method.Signature.ParameterTypes.Length + (working.Method.Signature.HasThis ? 1 : 0);
		foreach (IlInstruction instr in instrs)
			switch (instr.Operand) {
			case IlArgumentOperand argument when (uint)argument.Index >= (uint)argumentCount:
				throw new IlPipelineException($"{instr.Id} references argument {argument.Index}, but the method has {argumentCount} IL arguments");
			case IlLocalOperand local when (uint)local.Index >= (uint)working.Locals.Length:
				throw new IlPipelineException($"{instr.Id} references local {local.Index}, but the method has {working.Locals.Length} locals");
			}
	}

	private static bool allFromOwner(ReadOnlySpan<IlInstruction> instrs, int ownerIdx) {
		foreach (IlInstruction instr in instrs)
			if (instr.Provenance.OwnerIdIndex != ownerIdx)
				return false;
		return true;
	}

	private static bool allUniform(ReadOnlySpan<IlInstruction> instrs) {
		if (instrs.IsEmpty)
			return false;
		int firstOwnerIdx = instrs[0].Provenance.OwnerIdIndex;
		if (firstOwnerIdx is 0)
			return false;
		for (int i = 1; i < instrs.Length; i++)
			if (instrs[i].Provenance.OwnerIdIndex != firstOwnerIdx)
				return false;
		return true;
	}

	private static void setAnchor(List<IlAnchorId?> anchors, int boundary, IlAnchorId anchor) {
		IlAnchorId? existing = anchors[boundary];
		if (existing is not null && existing.Value != anchor)
			throw new InternalStateException($"two distinct anchors were assigned to output boundary {boundary}");
		anchors[boundary] = anchor;
	}

	private static IlAnchorId getLabelTarget(Dictionary<int, IlAnchorId> labelTargets, int labelId) {
		if (!labelTargets.TryGetValue(labelId, out IlAnchorId target))
			throw new InternalStateException($"marked label {labelId} has no resolved output anchor");
		return target;
	}

	private static void validatePattern(ReadOnlySpan<IlPatternElement> pattern, string paramName) {
		if (pattern.IsEmpty)
			throw new ArgumentException("pattern must not be empty", paramName);
		for (int i = 0; i < pattern.Length; i++)
			if (!pattern[i].IsValid)
				throw new ArgumentException($"pattern element {i} is invalid/uninitialized", paramName);
	}

	private static void validateProvenanceConstraint(IlPatternProvenanceConstraint constraint, string paramName) {
		if (constraint.Kind == IlPatternProvenanceConstraint.ConstraintKind.UninitializedValue)
			throw new ArgumentException("provenance constraint is invalid/uninitialized", paramName);
	}
}
