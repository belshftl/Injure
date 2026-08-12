// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Represents a structural method reference or generic method instantiation.
/// </summary>
public sealed class IlMethodRef : IEquatable<IlMethodRef> {
	/// <summary>
	/// The declaring type, which may itself be a generic instantiation.
	/// </summary>
	public IlTypeRef DeclaringType { get; }

	/// <summary>
	/// The metadata method name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The method definition signature.
	/// </summary>
	public IlMethodSignature Signature { get; }

	/// <summary>
	/// The generic method arguments, or an empty array for a non-instantiated method.
	/// </summary>
	public ImmutableArray<IlTypeRef> GenericArguments { get; }

	internal IlMethodRef(
		IlTypeRef declaringType,
		string name,
		IlMethodSignature signature,
		ImmutableArray<IlTypeRef> genericArguments
	) {
		DeclaringType = declaringType;
		Name = name;
		Signature = signature;
		GenericArguments = genericArguments;
	}

	/// <summary>
	/// Whether this reference represents a generic method instantiation.
	/// </summary>
	public bool IsGenericInstantiation => !GenericArguments.IsDefaultOrEmpty;

	public bool Equals(IlMethodRef? other) => IlReferenceEquality.MethodEquals(this, other);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlMethodRef other && Equals(other);
	public override int GetHashCode() => IlReferenceEquality.MethodHashCode(this);
	public static bool operator ==(IlMethodRef? left, IlMethodRef? right) => left is null ? right is null : left.Equals(right);
	public static bool operator !=(IlMethodRef? left, IlMethodRef? right) => !(left == right);

	public override string ToString() => $"{DeclaringType}::{Name}";
}
