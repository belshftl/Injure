// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods;

namespace Injure.Sched.Coro;

public readonly struct CoroInfo {
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
}
