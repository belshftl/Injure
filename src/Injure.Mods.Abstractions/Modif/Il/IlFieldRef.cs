// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Represents a structural field reference.
/// </summary>
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
