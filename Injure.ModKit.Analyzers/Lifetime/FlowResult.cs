// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class FlowResult {
	public FlowState? ContinueState { get; init; }
	public List<ControlFlowExit> Exits { get; } = new();
	public bool MayContinue => ContinueState is not null;

	public static FlowResult Continue(FlowState state) => new() { ContinueState = state };

	public static FlowResult Stop(ControlFlowExit exit) {
		FlowResult result = new();
		result.Exits.Add(exit);
		return result;
	}

	public static FlowResult NoContinue() => new();

	public static FlowResult Merge(FlowResult left, FlowResult right, Location location, FlowMergeKind kind) {
		FlowState? continueState = null;

		if (left.ContinueState is not null && right.ContinueState is not null)
			continueState = FlowState.MergeWorst(left.ContinueState, right.ContinueState, location, kind);
		else if (left.ContinueState is not null)
			continueState = left.ContinueState.Clone();
		else if (right.ContinueState is not null)
			continueState = right.ContinueState.Clone();

		FlowResult result = new() {
			ContinueState = continueState,
		};

		foreach (ControlFlowExit exit in left.Exits)
			result.Exits.Add(exit.Clone());
		foreach (ControlFlowExit exit in right.Exits)
			result.Exits.Add(exit.Clone());

		return result;
	}
}
