// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Input;

/// <summary>
/// Identifies an action registered in an <see cref="ActionRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Also stamped with what registry minted it, so IDs from different registries never compare equal.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct ActionId : IEquatable<ActionId> {
	/// <summary>
	/// ID of the registry that minted this. 0 only for <see langword="default"/>.
	/// </summary>
	internal readonly ulong RegistryId;

	/// <summary>
	/// The registry-local ID. 0 only for <see langword="default"/>.
	/// </summary>
	internal readonly uint Value;

	internal ActionId(ulong registryId, uint value) {
		RegistryId = registryId;
		Value = value;
	}

	/// <summary>
	/// Whether this is a valid ID, i.e. not <see langword="default"/>.
	/// </summary>
	public bool IsValid => Value != 0;

	/// <summary>
	/// Whether this and <paramref name="other"/> are the same ID. IDs from different registries are
	/// never equal.
	/// </summary>
	public bool Equals(ActionId other) => RegistryId == other.RegistryId && Value == other.Value;
	/// <summary>
	/// Equivalent to <see cref="Equals(ActionId)"/> if <paramref name="obj"/> is a
	/// <see cref="ActionId"/>; otherwise, <see langword="false"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is ActionId other && Equals(other);
	/// <summary>Computes a hash consistent with <see cref="Equals(ActionId)"/>.</summary>
	public override int GetHashCode() => HashCode.Combine(RegistryId, Value);
	/// <summary>Equivalent to <see cref="Equals(ActionId)"/>.</summary>
	public static bool operator ==(ActionId left, ActionId right) => left.Equals(right);
	/// <summary>Equivalent to the negation of <see cref="Equals(ActionId)"/>.</summary>
	public static bool operator !=(ActionId left, ActionId right) => !left.Equals(right);
}
