// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

using Injure.Common;

namespace Injure.Sched.Coro;

public readonly struct CoroYield {
	private readonly OneOf<IEnumerator<CoroYield>, CoroWait> inner;
	private CoroYield(IEnumerator<CoroYield> coro) {
		inner = OneOf.First(coro);
	}
	private CoroYield(CoroWait wait) {
		inner = OneOf.Second(wait);
	}

	public OneOf<IEnumerator<CoroYield>, CoroWait> Value => inner;

	public static implicit operator CoroYield(CoroWait wait) => new(wait);
	// apparently you can't have user-defined casts to/from an interface, even if the target type
	// is a visibly-unrelated struct, for some reason
	// public static implicit operator CoroYield(IEnumerator<CoroYield> coro) => new(coro);
	public static CoroYield Coroutine(IEnumerator<CoroYield> coro) => new(coro);
}
