// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// One instruction as declared by a manipulator, before labels and metadata references are
/// resolved.
/// </summary>
internal readonly record struct IlInstructionSpec(
	ILOpCode OpCode,
	IlOperand Operand,
	IlLabel[]? Labels
) {
	public static IlInstructionSpec Raw(ILOpCode opCode, IlOperand? operand = null) {
		if (IlOpCodeInfo.IsBranch(opCode) || opCode == ILOpCode.Switch)
			throw new ArgumentException("raw branch/switch emission is not supported; use the label-aware emission methods", nameof(opCode));
		ILOpCode canonical = IlOpCodeInfo.Canonicalize(opCode);
		if (canonical != opCode)
			throw new ArgumentException("compact encoding opcodes are not accepted by Raw; emit the canonical opcode and semantic operand", nameof(opCode));
		IlOperand normalizedOperand = operand ?? IlNoneOperand.Instance;
		validateOperand(canonical, normalizedOperand);
		return new IlInstructionSpec(canonical, normalizedOperand, null);
	}

	public static IlInstructionSpec Branch(ILOpCode opCode, IlLabel target) {
		ILOpCode canonical = IlOpCodeInfo.Canonicalize(opCode);
		if (!IlOpCodeInfo.IsBranch(opCode) && !IlOpCodeInfo.IsBranch(canonical))
			throw new ArgumentException($"{opCode} is not a branch opcode", nameof(opCode));
		return new IlInstructionSpec(canonical, IlNoneOperand.Instance, [target]);
	}

	public static IlInstructionSpec Switch(ReadOnlySpan<IlLabel> targets) =>
		new(ILOpCode.Switch, IlNoneOperand.Instance, targets.ToArray());

	private static void validateOperand(ILOpCode opCode, IlOperand operand) {
		IlOperandEncoding encoding = IlOpCodeInfo.GetOperandEncoding(opCode);
		bool valid = encoding switch {
			IlOperandEncoding.None => ReferenceEquals(operand, IlNoneOperand.Instance),
			IlOperandEncoding.Int8 or IlOperandEncoding.UInt8 or IlOperandEncoding.Int32 => operand is IlInt32Operand,
			IlOperandEncoding.Int64 => operand is IlInt64Operand,
			IlOperandEncoding.Float32 => operand is IlFloat32Operand,
			IlOperandEncoding.Float64 => operand is IlFloat64Operand,
			IlOperandEncoding.Argument8 or IlOperandEncoding.Argument16 => operand is IlArgumentOperand,
			IlOperandEncoding.Local8 or IlOperandEncoding.Local16 => operand is IlLocalOperand,
			IlOperandEncoding.StringToken => operand is IlStringOperand,
			IlOperandEncoding.TypeToken => operand is IlTypeOperand,
			IlOperandEncoding.MethodToken => operand is IlMethodOperand,
			IlOperandEncoding.FieldToken => operand is IlFieldOperand,
			IlOperandEncoding.SignatureToken => operand is IlCallSiteOperand,
			IlOperandEncoding.EntityToken => operand is IlTypeOperand or IlMethodOperand or IlFieldOperand,
			IlOperandEncoding.Branch8 or IlOperandEncoding.Branch32 or IlOperandEncoding.Switch => false,
			_ => false,
		};
		if (!valid)
			throw new ArgumentException($"operand {operand.GetType().Name} is invalid for opcode {opCode}", nameof(operand));
	}
}

internal abstract record IlFragmentNode;
internal sealed record IlFragmentInstructionNode(IlInstructionSpec Spec) : IlFragmentNode;
internal sealed record IlFragmentLabelNode(int LabelId) : IlFragmentNode;

internal readonly record struct IlFragment(
	IlFragmentNode[] Nodes,
	int[] ReferencedLabels,
	int[] MarkedLabels
);

/// <summary>
/// Accumulates the instructions and label marks of one emitted fragment.
/// </summary>
/// <remarks>
/// Records nodes in emit order and performs some checks that don't need the rest of the transaction,
/// like label ownership and duplicate marks within this fragment. Marks that collide across fragments
/// and labels that were never marked anywhere are caught at commit time.
/// </remarks>
internal sealed class IlFragmentBuilder(ulong transactionId, HashSet<int> knownLabels) {
	private readonly ulong transactionId = transactionId;
	private readonly HashSet<int> knownLabels = knownLabels;
	private readonly List<IlFragmentNode> nodes = new();
	private readonly HashSet<int> referencedLabels = new();
	private readonly HashSet<int> markedLabels = new();

	public void Emit(IlInstructionSpec spec) {
		foreach (IlLabel label in spec.Labels ?? [])
			referencedLabels.Add(validateLabel(label));
		nodes.Add(new IlFragmentInstructionNode(spec));
	}

	public void MarkLabel(IlLabel label) {
		int labelId = validateLabel(label);
		if (!markedLabels.Add(labelId))
			throw new IlPipelineException($"IL label {labelId} is marked more than once in the same fragment");
		nodes.Add(new IlFragmentLabelNode(labelId));
	}

	public IlFragment Finish() => new(nodes.ToArray(), referencedLabels.ToArray(), markedLabels.ToArray());

	private int validateLabel(IlLabel label) {
		if (label.TransactionId == 0 || label.TransactionId != transactionId)
			throw new IlPipelineException("IL label belongs to another manipulation transaction");
		if (!knownLabels.Contains(label.LabelId))
			throw new IlPipelineException($"unknown IL label {label.LabelId}");
		return label.LabelId;
	}
}
