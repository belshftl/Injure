// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal abstract record IlFragmentNode;
internal sealed record IlFragmentInstructionNode(IlInstructionSpec Spec) : IlFragmentNode;
internal sealed record IlFragmentLabelNode(int LabelId) : IlFragmentNode;

internal sealed class IlFragment(IlFragmentNode[] nodes, int[] referencedLabels, int[] markedLabels) : IStrongRefDroppable {
	private IlFragmentNode[] nodes = nodes;
	private int[] referencedLabels = referencedLabels;
	private int[] markedLabels = markedLabels;

	public IlFragmentNode[] Nodes => nodes;
	public int[] ReferencedLabels => referencedLabels;
	public int[] MarkedLabels => markedLabels;

	public void DropStrongReferences() {
		nodes = Array.Empty<IlFragmentNode>();
		referencedLabels = Array.Empty<int>();
		markedLabels = Array.Empty<int>();
	}
}

internal sealed class IlFragmentBuilder(long transactionId, HashSet<int> knownLabels) : IStrongRefDroppable {
	private readonly long transactionId = transactionId;
	private readonly HashSet<int> knownLabels = knownLabels;
	private readonly List<IlFragmentNode> nodes = new();
	private readonly HashSet<int> referencedLabels = new();
	private readonly HashSet<int> markedLabels = new();

	public void Emit(IlInstructionSpec spec) {
		foreach (IlLabel label in spec.GetReferencedLabels())
			referencedLabels.Add(validateLabel(label));
		nodes.Add(new IlFragmentInstructionNode(spec));
	}

	public void MarkLabel(IlLabel label) {
		int labelId = validateLabel(label);
		if (!markedLabels.Add(labelId))
			throw new IlPipelineException($"IL label {labelId} is marked more than once in the same emission fragment");
		nodes.Add(new IlFragmentLabelNode(labelId));
	}

	public IlFragment Finish() => new(nodes.ToArray(), referencedLabels.ToArray(), markedLabels.ToArray());

	public void DropStrongReferences() {
		nodes.Clear();
		referencedLabels.Clear();
		markedLabels.Clear();
	}

	private int validateLabel(IlLabel label) {
		if (label.TransactionId == 0 || label.TransactionId != transactionId)
			throw new IlPipelineException("IL label belongs to another manipulation transaction");
		if (!knownLabels.Contains(label.LabelId))
			throw new IlPipelineException($"unknown IL label {label.LabelId}");
		return label.LabelId;
	}
}
