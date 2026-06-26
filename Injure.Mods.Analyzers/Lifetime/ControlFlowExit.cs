// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Lifetime;

internal sealed class ControlFlowExit {
	public required ControlExitKind Kind { get; init; }
	public required FlowState State { get; init; }
	public required Location Location { get; init; }
	public ILabelSymbol? Target { get; init; }
	public ObligationTransitionCause Cause { get; init; }
	public ITypeSymbol? ExceptionType { get; init; }
	public bool ExceptionTypeUnknown { get; init; }

	public ControlFlowExit Clone() => new() {
		Kind = Kind,
		State = State.Clone(),
		Location = Location,
		Target = Target,
		Cause = Cause,
		ExceptionType = ExceptionType,
		ExceptionTypeUnknown = ExceptionTypeUnknown,
	};
}
