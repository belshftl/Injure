// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Base type for IL type references.
/// </summary>
/// <para>
/// Equality is structural and includes the scope, so the same type resolved through two different
/// modules is not equal. Pattern matching uses a slightly looser comparison.
/// </para>
public abstract class IlTypeRef : IEquatable<IlTypeRef> {
	private protected IlTypeRef() {
	}

	/// <summary>
	/// This type with every custom modifier stripped.
	/// </summary>
	public IlTypeRef Unmodified {
		get {
			IlTypeRef type = this;
			while (type is IlModifiedTypeRef modified)
				type = modified.UnmodifiedType;
			return type;
		}
	}

	/// <summary>
	/// Whether this type is the CLI <see langword="void"/> type, ignoring any custom modifiers.
	/// </summary>
	public bool IsVoid => Unmodified is IlPrimitiveTypeRef { Code: PrimitiveTypeCode.Void };

	/// <summary>
	/// Checks for structural equality with <paramref name="other"/>.
	/// </summary>
	public bool Equals(IlTypeRef? other) => IlRefEquality.TypeEquals(this, other);

	/// <summary>
	/// Checks for structural equality with <paramref name="obj"/> if it is an <see cref="IlTypeRef"/>.
	/// </summary>
	public sealed override bool Equals([NotNullWhen(true)] object? obj) => obj is IlTypeRef other && Equals(other);

	/// <summary>
	/// Gets a structural hash in correspondence with <see cref="Equals(IlTypeRef?)"/>.
	/// </summary>
	public sealed override int GetHashCode() => IlRefEquality.TypeHashCode(this);

	public static bool operator ==(IlTypeRef? left, IlTypeRef? right) => left is null ? right is null : left.Equals(right);
	public static bool operator !=(IlTypeRef? left, IlTypeRef? right) => !(left == right);
}

/// <summary>
/// Represents a CLI primitive type.
/// </summary>
public sealed class IlPrimitiveTypeRef : IlTypeRef {
	/// <summary>
	/// The primitive type code.
	/// </summary>
	public PrimitiveTypeCode Code { get; }

	internal IlPrimitiveTypeRef(PrimitiveTypeCode code) => Code = code;

	public override string ToString() => Code.ToString();
}

/// <summary>
/// Specifies how a named type is encoded in signatures.
/// </summary>
[ClosedEnum]
public readonly partial struct IlNamedTypeKind {
	/// <summary>Raw switch tag for <see cref="IlNamedTypeKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// The class/value-type distinction is not known.
		/// </summary>
		Unknown,

		/// <summary>
		/// The named type is encoded as a class.
		/// </summary>
		Class,

		/// <summary>
		/// The named type is encoded as a value type.
		/// </summary>
		ValueType,
	}
}

/// <summary>
/// Represents a named type definition or reference.
/// </summary>
public sealed class IlNamedTypeRef : IlTypeRef {
	/// <summary>
	/// The metadata resolution scope.
	/// </summary>
	public IlTypeScope Scope { get; }

	/// <summary>
	/// The declaring type for a nested type, or <see langword="null"/>.
	/// </summary>
	public IlNamedTypeRef? DeclaringType { get; }

	/// <summary>
	/// The namespace for a top-level type.
	/// </summary>
	public string Namespace { get; }

	/// <summary>
	/// The metadata name without the arity suffix.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The number of generic parameters declared by this type.
	/// </summary>
	public int GenericArity { get; }

	/// <summary>
	/// The signature encoding kind.
	/// </summary>
	public IlNamedTypeKind TypeKind { get; }

	internal IlNamedTypeRef(
		IlTypeScope scope,
		IlNamedTypeRef? declaringType,
		string @namespace,
		string name,
		int genericArity,
		IlNamedTypeKind typeKind
	) {
		Scope = scope;
		DeclaringType = declaringType;
		Namespace = @namespace;
		Name = name;
		GenericArity = genericArity;
		TypeKind = typeKind;
	}

	public override string ToString() {
		string own = GenericArity == 0 ? Name : $"{Name}`{GenericArity}";
		if (DeclaringType is not null)
			return $"{DeclaringType}+{own}";
		return Namespace.Length == 0 ? own : $"{Namespace}.{own}";
	}
}

/// <summary>
/// Specifies whether a generic parameter belongs to a type or method.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct IlGenericParameterKind {
	/// <summary>Raw switch tag for <see cref="IlGenericParameterKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// A type generic parameter (<c>!n</c>).
		/// </summary>
		Type = 1,

		/// <summary>
		/// A method generic parameter (<c>!!n</c>).
		/// </summary>
		Method,
	}
}

/// <summary>
/// Represents a type or method generic parameter.
/// </summary>
public sealed class IlGenericParameterTypeRef : IlTypeRef {
	/// <summary>
	/// The generic parameter owner kind.
	/// </summary>
	public IlGenericParameterKind Kind { get; }

	/// <summary>
	/// The generic parameter index.
	/// </summary>
	public int Index { get; }

	internal IlGenericParameterTypeRef(IlGenericParameterKind kind, int index) {
		Kind = kind;
		Index = index;
	}

	public override string ToString() => Kind == IlGenericParameterKind.Type ? $"!{Index}" : $"!!{Index}";
}

/// <summary>
/// Represents an instantiated generic type.
/// </summary>
public sealed class IlGenericInstanceTypeRef : IlTypeRef {
	/// <summary>
	/// The generic type definition reference.
	/// </summary>
	public IlNamedTypeRef GenericType { get; }

	/// <summary>
	/// The generic arguments, in order.
	/// </summary>
	public ImmutableArray<IlTypeRef> Arguments { get; }

	internal IlGenericInstanceTypeRef(IlNamedTypeRef genericType, ImmutableArray<IlTypeRef> arguments) {
		GenericType = genericType;
		Arguments = arguments;
	}

	public override string ToString() => $"{GenericType}<{string.Join(", ", Arguments)}>";
}

/// <summary>
/// Represents an array type that is not an szarray (higher rank, non-zero lower bound, etc.)
/// </summary>
public sealed class IlArrayTypeRef : IlTypeRef {
	/// <summary>
	/// The element type.
	/// </summary>
	public IlTypeRef ElementType { get; }

	/// <summary>
	/// The array rank.
	/// </summary>
	public int Rank { get; }

	/// <summary>
	/// The specified dimension sizes.
	/// </summary>
	public ImmutableArray<int> Sizes { get; }

	/// <summary>
	/// The specified lower bounds.
	/// </summary>
	public ImmutableArray<int> LowerBounds { get; }

	internal IlArrayTypeRef(
		IlTypeRef elementType,
		int rank,
		ImmutableArray<int> sizes,
		ImmutableArray<int> lowerBounds
	) {
		ElementType = elementType;
		Rank = rank;
		Sizes = sizes.IsDefault ? [] : sizes;
		LowerBounds = lowerBounds.IsDefault ? [] : lowerBounds;
	}

	public override string ToString() => $"{ElementType}[{new string(',', Math.Max(0, Rank - 1))}]";
}

/// <summary>
/// Represents an szarray (single-dimension, zero-indexed array) type.
/// </summary>
public sealed class IlSzArrayTypeRef : IlTypeRef {
	/// <summary>
	/// The element type.
	/// </summary>
	public IlTypeRef ElementType { get; }

	internal IlSzArrayTypeRef(IlTypeRef elementType) => ElementType = elementType;

	public override string ToString() => $"{ElementType}[]";
}

/// <summary>
/// Represents an unmanaged pointer type.
/// </summary>
public sealed class IlPointerTypeRef : IlTypeRef {
	/// <summary>
	/// The pointee type.
	/// </summary>
	public IlTypeRef ElementType { get; }

	internal IlPointerTypeRef(IlTypeRef elementType) => ElementType = elementType;

	public override string ToString() => $"{ElementType}*";
}

/// <summary>
/// Represents a managed byref type.
/// </summary>
public sealed class IlByRefTypeRef : IlTypeRef {
	/// <summary>
	/// The referenced type.
	/// </summary>
	public IlTypeRef ElementType { get; }

	internal IlByRefTypeRef(IlTypeRef elementType) => ElementType = elementType;

	public override string ToString() => $"{ElementType}&";
}

/// <summary>
/// Represents a pinned local variable type.
/// </summary>
/// <remarks>
/// "Pinned" is a property of a local variable type and is only relevant when decoding metadata
/// signatures, so this type is internal.
/// </remarks>
internal sealed class IlPinnedTypeRef : IlTypeRef {
	/// <summary>
	/// The pinned type.
	/// </summary>
	public IlTypeRef ElementType { get; }

	internal IlPinnedTypeRef(IlTypeRef elementType) => ElementType = elementType;

	public override string ToString() => $"pinned {ElementType}";
}

/// <summary>
/// Represents a custom-modified type.
/// </summary>
public sealed class IlModifiedTypeRef : IlTypeRef {
	/// <summary>
	/// The modifier type.
	/// </summary>
	public IlTypeRef Modifier { get; }

	/// <summary>
	/// The type to which the modifier applies.
	/// </summary>
	public IlTypeRef UnmodifiedType { get; }

	/// <summary>
	/// Whether the modifier is required rather than optional.
	/// </summary>
	public bool IsRequired { get; }

	internal IlModifiedTypeRef(IlTypeRef modifier, IlTypeRef unmodifiedType, bool isRequired) {
		Modifier = modifier;
		UnmodifiedType = unmodifiedType;
		IsRequired = isRequired;
	}

	public override string ToString() => $"{UnmodifiedType} mod{(IsRequired ? "req" : "opt")}({Modifier})";
}

/// <summary>
/// Represents a function pointer type.
/// </summary>
public sealed class IlFunctionPointerTypeRef : IlTypeRef {
	/// <summary>
	/// The function pointer signature.
	/// </summary>
	public IlMethodSignature Signature { get; }

	internal IlFunctionPointerTypeRef(IlMethodSignature signature) => Signature = signature;

	public override string ToString() => $"method {Signature}*";
}

/// <summary>
/// Represents the metadata <c>&lt;Module&gt;</c> pseudo-type.
/// </summary>
/// <remarks>
/// The metadata <c>&lt;Module&gt;</c> pseudo-type is mostly useful for decoding or representing
/// global methods/fields and is not a regular signature type, so this type is internal.
/// </remarks>
internal sealed class IlGlobalModuleTypeRef : IlTypeRef {
	/// <summary>
	/// The containing metadata scope.
	/// </summary>
	public IlTypeScope Scope { get; }

	internal IlGlobalModuleTypeRef(IlTypeScope scope) => Scope = scope;

	public override string ToString() => "<Module>";
}
