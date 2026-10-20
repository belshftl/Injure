// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Sched.Coro;

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct CoroTraceFrame : IHasInvalidDefault {
	// explicit so that object initializers don't trip IJ0100; `required` makes them set every member
	public CoroTraceFrame() {}

	public required string DebugName { get; init; }
	public required string EnumeratorTypeName { get; init; }
	public required string SourceFile { get; init; }
	public required int SourceLine { get; init; }
	public required string SourceMember { get; init; }

	bool IHasInvalidDefault.IsDefault => DebugName is null;
}

public sealed class CoroTrace {
	public required CoroHandle Handle { get; init; }
	public required string? Name { get; init; }
	public required string? ScopeName { get; init; }
	public required string? CurrentWaitDebugDescription { get; init; }
	public required IReadOnlyList<CoroTraceFrame> Frames { get; init; }
}
