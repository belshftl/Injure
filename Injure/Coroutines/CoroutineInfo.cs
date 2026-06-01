// SPDX-License-Identifier: MIT

using Injure.ModKit.Abstractions;

namespace Injure.Coroutines;

public readonly struct CoroutineInfo {
	public required CoroutineHandle Handle { get; init; }
	public required string? Name { get; init; }
	public required string? OwnerID { get; init; }
	public required string? ScopeName { get; init; }
	public required CoroutineStatus Status { get; init; }
	public required CoroUpdatePhase LastPhase { get; init; }
	public required CoroutineTick StartTick { get; init; }
	public required CoroutineTick TerminalTick { get; init; }
	public required int StackDepth { get; init; }
	public required string? CurrentWaitDebugDescription { get; init; }
	public required ExceptionSnapshot? Fault { get; init; }
	public required CoroCancellationReason? CancellationReason { get; init; }
}
