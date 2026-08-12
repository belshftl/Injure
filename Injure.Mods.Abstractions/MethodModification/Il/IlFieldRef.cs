// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.MethodModification.Il;

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

	public bool Equals(IlFieldRef? other) => IlReferenceEquality.FieldEquals(this, other);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlFieldRef other && Equals(other);
	public override int GetHashCode() => IlReferenceEquality.FieldHashCode(this);
	public static bool operator ==(IlFieldRef? left, IlFieldRef? right) => left is null ? right is null : left.Equals(right);
	public static bool operator !=(IlFieldRef? left, IlFieldRef? right) => !(left == right);

	public override string ToString() => $"{DeclaringType}::{Name}";
}
