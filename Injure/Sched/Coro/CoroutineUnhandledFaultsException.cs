// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Injure.Sched.Coro;

public sealed class CoroutineUnhandledFaultsException(IReadOnlyList<CoroutineUnhandledFaultInfo> faults) : Exception($"{faults.Count} unhandled coroutine faults occurred") {
	public IReadOnlyList<CoroutineUnhandledFaultInfo> Faults { get; } = faults;
}
