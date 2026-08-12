// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions;

namespace Injure.Mods.Runtime;

internal sealed class Retention<T>(T val) : IDisposable, IStrongRefDroppable where T : class {
	private T? val = val;
	public void Dispose() => DropStrongReferences();
	public void DropStrongReferences() => Volatile.Write(ref val, null);
}
