// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Half-open instruction range.
/// </summary>
internal readonly struct IlRange : IEquatable<IlRange> {
	public int Start { get; }
	public int End { get; }
	public int Length => End - Start;

	public IlRange(int start, int end) {
		if (start > end)
			throw new InternalStateException("IlRange has its start past its end");
		Start = start;
		End = end;
	}

	public bool Equals(IlRange other) => Start == other.Start && End == other.End;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlRange other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Start, End);
	public static bool operator ==(IlRange left, IlRange right) => left.Equals(right);
	public static bool operator !=(IlRange left, IlRange right) => !left.Equals(right);
}
