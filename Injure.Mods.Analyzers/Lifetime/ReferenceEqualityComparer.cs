// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Injure.Mods.Analyzers.Lifetime;

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class {
	public static ReferenceEqualityComparer<T> Instance { get; } = new();
	private ReferenceEqualityComparer() {}
	public bool Equals(T? left, T? right) => ReferenceEquals(left, right);
	public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
