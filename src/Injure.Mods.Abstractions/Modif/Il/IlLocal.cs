// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.CodeAnalysis.Internal;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Opaque transaction-local handle to a declared local, usable only by the transaction that
/// declared it.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[DontCache("IlLocal objects are only valid within the same IL manipulator transaction that minted them")]
public readonly struct IlLocal : IEquatable<IlLocal> {
	/// <summary>
	/// The index that will be emitted for this local. Final at declaration; once the transaction
	/// commits, later manipulators see an ordinary local at this index. It never changes; locals are
	/// only ever appended. There is no API for removing locals and will likely never be.
	/// </summary>
	public int Index { get; }

	internal ulong TransactionId { get; }

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
