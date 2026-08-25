// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Sched.Coro;

public readonly struct CoroHandle(int slot, int generation) : IEquatable<CoroHandle> {
	public static readonly CoroHandle Invalid = default;
	public int Slot { get; } = slot;
	public int Generation { get; } = generation;
	public readonly bool IsValid => Generation > 0;

	public bool Equals(CoroHandle other) => Slot == other.Slot && Generation == other.Generation;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CoroHandle other && Equals(other);
	public override int GetHashCode() => unchecked(Slot * 397 ^ Generation);
	public static bool operator ==(CoroHandle left, CoroHandle right) => left.Equals(right);
	public static bool operator !=(CoroHandle left, CoroHandle right) => !left.Equals(right);
}
