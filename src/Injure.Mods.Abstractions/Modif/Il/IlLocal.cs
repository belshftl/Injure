// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.CodeAnalysis.Internal;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Opaque handle to a local declared by a manipulation transaction.
/// </summary>
/// <remarks>
/// <para>
/// Usable only by the transaction that declared it. The index is final at declaration, so once the
/// transaction commits, later manipulators see it as an ordinary index.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[DontCache("IlLocal objects are only valid within the same IL manipulator transaction that minted them")]
/* pending */ internal readonly struct IlLocal : IEquatable<IlLocal> {
	internal ulong TransactionId { get; }
	internal int Index { get; }

	internal IlLocal(ulong transactionId, int index) {
		TransactionId = transactionId;
		Index = index;
	}

	public bool Equals(IlLocal other) => TransactionId == other.TransactionId && Index == other.Index;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlLocal other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(TransactionId, Index);
	public static bool operator ==(IlLocal left, IlLocal right) => left.Equals(right);
	public static bool operator !=(IlLocal left, IlLocal right) => !left.Equals(right);
}
