// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Represents a structural method or standalone callsite signature.
/// </summary>
public sealed class IlMethodSignature : IEquatable<IlMethodSignature> {
	/// <summary>
	/// The unmanaged or managed calling convention.
	/// </summary>
	public SignatureCallingConvention CallingConvention { get; }

	/// <summary>
	/// Whether the signature includes an instance receiver.
	/// </summary>
	public bool HasThis { get; }

	/// <summary>
	/// Whether the instance pointer is declared as the first entry of <see cref="ParameterTypes"/>
	/// rather than implied.
	/// </summary>
	/// <remarks>
	/// Only meaningful when <see cref="HasThis"/> is set. When both are set, a call site pushes exactly
	/// the values in <see cref="ParameterTypes"/> and nothing extra; when only <see cref="HasThis"/> is
	/// set, it pushes an additional receiver first.
	/// </remarks>
	public bool ExplicitThis { get; }

	/// <summary>
	/// The number of method generic parameters.
	/// </summary>
	public int GenericParameterCount { get; }

	/// <summary>
	/// The number of required parameters before optional varargs parameters.
	/// </summary>
	public int RequiredParameterCount { get; }

	/// <summary>
	/// The return type.
	/// </summary>
	public IlTypeRef ReturnType { get; }

	/// <summary>
	/// Every parameter type in order, required parameters first, then any optional ones a vararg call
	/// site supplies. The split point is <see cref="RequiredParameterCount"/>.
	/// </summary>
	/// <remarks>
	/// This is the full count of values a call site pops for its arguments, so it includes optional
	/// parameters even though a method definition's signature never has them.
	/// </remarks>
	public ImmutableArray<IlTypeRef> ParameterTypes { get; }

	internal IlMethodSignature(
		SignatureCallingConvention callingConvention,
		bool hasThis,
		bool explicitThis,
		int genericParameterCount,
		int requiredParameterCount,
		IlTypeRef returnType,
		ImmutableArray<IlTypeRef> parameterTypes
	) {
		CallingConvention = callingConvention;
		HasThis = hasThis;
		ExplicitThis = explicitThis;
		GenericParameterCount = genericParameterCount;
		RequiredParameterCount = requiredParameterCount;
		ReturnType = returnType;
		ParameterTypes = parameterTypes;
	}

	/// <summary>
	/// The number of optional varargs parameters represented by this signature.
	/// </summary>
	public int OptionalParameterCount => ParameterTypes.Length - RequiredParameterCount;

	/// <summary>
	/// Checks for structural equality with <paramref name="other"/>.
	/// </summary>
	public bool Equals(IlMethodSignature? other) => IlReferenceEquality.SignatureEquals(this, other);

	/// <summary>
	/// Checks for structural equality with <paramref name="obj"/> if it is an <see cref="IlMethodSignature"/>.
	/// </summary>
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlMethodSignature other && Equals(other);

	/// <summary>
	/// Gets a structural hash in correspondence with <see cref="Equals(IlMethodSignature?)"/>.
	/// </summary>
	public override int GetHashCode() => IlReferenceEquality.SignatureHashCode(this);

	public static bool operator ==(IlMethodSignature? left, IlMethodSignature? right) => left is null ? right is null : left.Equals(right);
	public static bool operator !=(IlMethodSignature? left, IlMethodSignature? right) => !(left == right);

	public override string ToString() => $"{ReturnType} ({string.Join(", ", ParameterTypes)})";
}
