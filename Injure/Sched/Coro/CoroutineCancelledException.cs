// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.Sched.Coro;

public sealed class CoroutineCancelledException(CoroutineHandle handle, CoroCancellationReason reason) : Exception($"coroutine {handle} cancelled: {reason}") {
	public CoroutineHandle Handle { get; } = handle;
	public CoroCancellationReason Reason { get; } = reason;
}
