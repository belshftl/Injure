// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods;

namespace Injure.Sched.Coro;

public sealed class CoroChildFaultException(CoroHandle handle, ExceptionSnapshot childFault) : Exception($"coroutine {handle} faulted", childFault.ToException()) {
	public CoroHandle Handle { get; } = handle;
	public ExceptionSnapshot ChildException { get; } = childFault;
}
