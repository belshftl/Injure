// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;
using Injure.Mods;

namespace Injure.Sched.Coro;

[ClosedEnum]
public readonly partial struct CoroUnhandledFaultMode {
	public enum Case {
		Ignore,
		LogAfterTick,
		ThrowAfterTick,
		LogAndThrowAfterTick,
	}
}

public sealed class CoroutineUnhandledFaultInfo {
	public required ExceptionSnapshot Exception { get; init; }
	public required CoroutineInfo Info { get; init; }
	public required CoroutineTrace Trace { get; init; }
}
