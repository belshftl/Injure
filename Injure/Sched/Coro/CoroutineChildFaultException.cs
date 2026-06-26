// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

using Injure.Mods;

namespace Injure.Sched.Coro;

public sealed class CoroutineChildFaultException(CoroutineHandle handle, ExceptionSnapshot childFault) : Exception($"coroutine {handle} faulted", childFault.ToException()) {
	public CoroutineHandle Handle { get; } = handle;
	public ExceptionSnapshot ChildException { get; } = childFault;
}
