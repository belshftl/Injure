// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Represents a structural field reference. New instances are created through <see cref="IlRefFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The difference between <see cref="System.Reflection.FieldInfo"/> and <see cref="IlFieldRef"/> is
/// that a <see cref="System.Reflection.FieldInfo"/> is a real field that is part of a currently loaded
/// type and exists at runtime, whereas <see cref="IlFieldRef"/> is pure metadata, consisting of the
/// declaring type's metadata, field name, and field type's metadata.
/// </para>
/// <para>
/// This means that a <see cref="System.Reflection.FieldInfo"/> can be "converted" to
/// <see cref="IlFieldRef"/>, but the reverse is impossible; it would be like trying to convert
/// (a fancier form of) the name of a field to the field itself.
/// </para>
/// </remarks>
public sealed class IlFieldRef : IEquatable<IlFieldRef> {
	/// <summary>
	/// The declaring type.
	/// </summary>
	public IlTypeRef DeclaringType { get; }

	/// <summary>
	/// The metadata field name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The field type.
	/// </summary>
	public IlTypeRef FieldType { get; }

	internal IlFieldRef(IlTypeRef declaringType, string name, IlTypeRef fieldType) {
		DeclaringType = declaringType;
		Name = name;
		FieldType = fieldType;
	}

	/// <summary>
	/// Checks for structural equality with <paramref name="other"/>.
	/// </summary>
	public bool Equals(IlFieldRef? other) => IlRefEquality.FieldEquals(this, other);

	/// <summary>
	/// Checks for structural equality with <paramref name="obj"/> if it is an <see cref="IlFieldRef"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlFieldRef other && Equals(other);

	/// <summary>
	/// Gets a structural hash in correspondence with <see cref="Equals(IlFieldRef?)"/>.
	/// </summary>
	public override int GetHashCode() => IlRefEquality.FieldHashCode(this);

	public static bool operator ==(IlFieldRef? left, IlFieldRef? right) => left is null ? right is null : left.Equals(right);
	public static bool operator !=(IlFieldRef? left, IlFieldRef? right) => !(left == right);

	public override string ToString() => $"{DeclaringType}::{Name}";
}
