// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Injure.Mods.Runtime;

internal sealed class ClearableDisposable<T>(T val) : IDisposable where T : class, IDisposable {
	private T? val = val;

	public void Dispose() {
		T? v = Interlocked.Exchange(ref val, null);
		v?.Dispose();
	}
}
