// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.CodeAnalysis;

namespace Injure.Mods.Hooks.Il;

/// <summary>
/// Opaque transaction-local label that may be referenced before its location is marked.
/// </summary>
/// <remarks>
/// <para>
/// A label is marked exactly once and may only be used by the manipulation transaction that
/// created it. Retaining a label after its manipulator returns is harmless, but it cannot be reused.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[DontCache]
public readonly struct IlLabel : IEquatable<IlLabel> {
	internal long TransactionId { get; }
	internal int LabelId { get; }
	internal IlLabel(long transactionId, int labelId) {
		TransactionId = transactionId;
		LabelId = labelId;
	}

	public bool Equals(IlLabel other) => TransactionId == other.TransactionId && LabelId == other.LabelId;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlLabel other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(TransactionId, LabelId);
	public static bool operator ==(IlLabel left, IlLabel right) => left.Equals(right);
	public static bool operator !=(IlLabel left, IlLabel right) => !left.Equals(right);
}
