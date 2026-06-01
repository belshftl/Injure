// SPDX-License-Identifier: MIT

using System;

using Injure.ModKit.Abstractions;

namespace Injure.Coroutines;

public sealed class CoroutineChildFaultException(CoroutineHandle handle, ExceptionSnapshot childFault) : Exception($"coroutine {handle} faulted", childFault.ToException()) {
	public CoroutineHandle Handle { get; } = handle;
	public ExceptionSnapshot ChildException { get; } = childFault;
}
