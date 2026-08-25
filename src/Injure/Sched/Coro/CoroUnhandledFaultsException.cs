// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Sched.Coro;

public sealed class CoroUnhandledFaultsException(IReadOnlyList<CoroUnhandledFaultInfo> faults) : Exception($"{faults.Count} unhandled coroutine faults occurred") {
	public IReadOnlyList<CoroUnhandledFaultInfo> Faults { get; } = faults;
}
