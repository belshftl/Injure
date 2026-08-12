// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Identifies an instruction boundary within a method body.
/// </summary>
/// <remarks>
/// <para>
/// An anchor names the gap before an instruction, not the instruction itself. A body with N
/// instructions has N+1 boundaries; the last one is the end of the body.
/// </para>
/// <para>
/// Anchors are stable across edits. An anchor keeps naming the same position relative to the
/// original instruction that followed it, no matter how much is inserted around it, which is what
/// lets exception regions and branch targets survive arbitrary manipulation with no adjustment.
/// Anchor IDs are unique within a body and are not reused; they are not an ordering key, since a
/// boundary's position is its index, not its ID.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct IlAnchorId : IEquatable<IlAnchorId> {
	/// <summary>
	/// The method-local ID value.
	/// </summary>
	public ulong Value { get; }

	/// <summary>
	/// Whether this value refers to an instruction boundary.
	/// </summary>
	public bool IsValid => Value > 0;

	internal IlAnchorId(ulong value) => Value = value;

	public bool Equals(IlAnchorId other) => Value == other.Value;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlAnchorId other && Equals(other);
	public override int GetHashCode() => Value.GetHashCode();
	public static bool operator ==(IlAnchorId left, IlAnchorId right) => left.Equals(right);
	public static bool operator !=(IlAnchorId left, IlAnchorId right) => !left.Equals(right);

	/// <summary>
	/// Formats this <see cref="IlAnchorId"/> into a string in the form <c>A123</c>.
	/// </summary>
	public override string ToString() => "A" + Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
