// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Represents a structural method reference or generic method instantiation.
/// New instances are created through <see cref="IlRefFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The difference between <see cref="System.Reflection.MethodInfo"/> and <see cref="IlMethodRef"/> is
/// that a <see cref="System.Reflection.MethodInfo"/> is a real method that is part of a currently loaded
/// type and exists at runtime, whereas <see cref="IlMethodRef"/> is pure metadata, consisting of the
/// declaring type's metadata, method name, signature, and generic arguments.
/// </para>
/// <para>
/// This means that a <see cref="System.Reflection.MethodInfo"/> can be "converted" to
/// <see cref="IlMethodRef"/>, but the reverse is impossible; it would be like trying to convert
/// (a fancier form of) the name of a method to the method itself.
/// </para>
/// </remarks>
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

	/// <summary>
	/// Checks for structural equality with <paramref name="other"/>.
	/// </summary>
	public bool Equals(IlMethodRef? other) => IlRefEquality.MethodEquals(this, other);

	/// <summary>
	/// Checks for structural equality with <paramref name="obj"/> if it is an <see cref="IlMethodRef"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlMethodRef other && Equals(other);

	/// <summary>
	/// Gets a structural hash in correspondence with <see cref="Equals(IlMethodRef?)"/>.
	/// </summary>
	public override int GetHashCode() => IlRefEquality.MethodHashCode(this);

	public static bool operator ==(IlMethodRef? left, IlMethodRef? right) => left is null ? right is null : left.Equals(right);
	public static bool operator !=(IlMethodRef? left, IlMethodRef? right) => !(left == right);

	public override string ToString() => $"{DeclaringType}::{Name}";
}
