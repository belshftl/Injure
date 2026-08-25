// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
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

public sealed class CoroUnhandledFaultInfo {
	public required ExceptionSnapshot Exception { get; init; }
	public required CoroInfo Info { get; init; }
	public required CoroTrace Trace { get; init; }
}
