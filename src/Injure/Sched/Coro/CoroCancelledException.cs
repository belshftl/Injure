// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Sched.Coro;

public sealed class CoroCancelledException(CoroHandle handle, CoroCancellationReason reason) : Exception($"coroutine {handle} cancelled: {reason}") {
	public CoroHandle Handle { get; } = handle;
	public CoroCancellationReason Reason { get; } = chk(reason);

	private static CoroCancellationReason chk(CoroCancellationReason reason) {
		ArgumentInvalidDefaultException.ThrowIfDefault(reason);
		return reason;
	}
}
