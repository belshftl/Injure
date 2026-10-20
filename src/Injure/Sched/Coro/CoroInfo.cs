// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods;

namespace Injure.Sched.Coro;

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct CoroInfo : IHasInvalidDefault {
	// explicit so that object initializers don't trip IJ0100; `required` makes them set every member
	public CoroInfo() {}

	public required CoroHandle Handle { get; init; }
	public required string? Name { get; init; }
	public required string? OwnerId { get; init; }
	public required string? ScopeName { get; init; }
	public required CoroStatus Status { get; init; }
	public required CoroUpdatePhase LastPhase { get; init; }
	public required CoroTick StartTick { get; init; }
	public required CoroTick TerminalTick { get; init; }
	public required int StackDepth { get; init; }
	public required string? CurrentWaitDebugDescription { get; init; }
	public required ExceptionSnapshot? Fault { get; init; }
	public required CoroCancellationReason? CancellationReason { get; init; }

	bool IHasInvalidDefault.IsDefault => Status == default;
}
