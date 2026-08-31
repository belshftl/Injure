// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.CodeAnalysis.Internal;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Opaque transaction-local branch target that may be referenced before its location is marked.
/// </summary>
/// <remarks>
/// <para>
/// A label is marked exactly once and may only be used by the manipulation transaction that
/// created it. Retaining a label after its manipulator returns is harmless, but it cannot be reused.
/// </para>
/// <para>
/// Referencing a label is always legal; the constraints are checked when the transaction commits,
/// not when the branch is emitted. Branching to a label that is never marked, marking one twice, and
/// using one minted by a different transaction all fail only at commit, so a manipulator is
/// free to emit a forward branch and mark its target later.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[DontCache("IlLabel objects are only valid within the same IL manipulator transaction that minted them")]
public readonly struct IlLabel : IEquatable<IlLabel> {
	internal ulong TransactionId { get; }
	internal int LabelId { get; }

	internal IlLabel(ulong transactionId, int labelId) {
		TransactionId = transactionId;
		LabelId = labelId;
	}

	public bool Equals(IlLabel other) => TransactionId == other.TransactionId && LabelId == other.LabelId;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlLabel other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(TransactionId, LabelId);
	public static bool operator ==(IlLabel left, IlLabel right) => left.Equals(right);
	public static bool operator !=(IlLabel left, IlLabel right) => !left.Equals(right);
}
