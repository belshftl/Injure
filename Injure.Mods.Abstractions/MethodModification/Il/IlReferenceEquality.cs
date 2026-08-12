// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal static class IlReferenceEquality {
	private sealed class TypeComparerImpl : IEqualityComparer<IlTypeRef> {
		public bool Equals(IlTypeRef? x, IlTypeRef? y) => TypeEquals(x, y);
		public int GetHashCode(IlTypeRef obj) => TypeHashCode(obj);
	}
	private sealed class ScopeComparerImpl : IEqualityComparer<IlTypeScope> {
		public bool Equals(IlTypeScope? x, IlTypeScope? y) => ScopeEquals(x, y);
		public int GetHashCode(IlTypeScope obj) => ScopeHashCode(obj);
	}
	private sealed class SignatureComparerImpl : IEqualityComparer<IlMethodSignature> {
		public bool Equals(IlMethodSignature? x, IlMethodSignature? y) => SignatureEquals(x, y);
		public int GetHashCode(IlMethodSignature obj) => SignatureHashCode(obj);
	}
	private sealed class MethodComparerImpl : IEqualityComparer<IlMethodRef> {
		public bool Equals(IlMethodRef? x, IlMethodRef? y) => MethodEquals(x, y);
		public int GetHashCode(IlMethodRef obj) => MethodHashCode(obj);
	}
	private sealed class FieldComparerImpl : IEqualityComparer<IlFieldRef> {
		public bool Equals(IlFieldRef? x, IlFieldRef? y) => FieldEquals(x, y);
		public int GetHashCode(IlFieldRef obj) => FieldHashCode(obj);
	}

	public static IEqualityComparer<IlTypeRef> TypeComparer { get; } = new TypeComparerImpl();
	public static IEqualityComparer<IlTypeScope> ScopeComparer { get; } = new ScopeComparerImpl();
	public static IEqualityComparer<IlMethodSignature> SignatureComparer { get; } = new SignatureComparerImpl();
	public static IEqualityComparer<IlMethodRef> MethodComparer { get; } = new MethodComparerImpl();
	public static IEqualityComparer<IlFieldRef> FieldComparer { get; } = new FieldComparerImpl();

	internal static bool TypeEquals(IlTypeRef? left, IlTypeRef? right) {
		if (ReferenceEquals(left, right))
			return true;
		if (left is null || right is null || left.GetType() != right.GetType())
			return false;
		return (left, right) switch {
			(IlPrimitiveTypeRef a, IlPrimitiveTypeRef b) => a.Code == b.Code,
			(IlNamedTypeRef a, IlNamedTypeRef b) =>
				ScopeEquals(a.Scope, b.Scope) && TypeEquals(a.DeclaringType, b.DeclaringType) &&
				a.Namespace == b.Namespace &&
				a.Name == b.Name &&
				a.GenericArity == b.GenericArity && a.TypeKind == b.TypeKind,
			(IlGenericParameterTypeRef a, IlGenericParameterTypeRef b) => a.Kind == b.Kind && a.Index == b.Index,
			(IlGenericInstanceTypeRef a, IlGenericInstanceTypeRef b) =>
				TypeEquals(a.GenericType, b.GenericType) && typeSequenceEquals(a.Arguments, b.Arguments),
			(IlArrayTypeRef a, IlArrayTypeRef b) =>
				TypeEquals(a.ElementType, b.ElementType) && a.Rank == b.Rank &&
				a.Sizes.AsSpan().SequenceEqual(b.Sizes.AsSpan()) &&
				a.LowerBounds.AsSpan().SequenceEqual(b.LowerBounds.AsSpan()),
			(IlSzArrayTypeRef a, IlSzArrayTypeRef b) => TypeEquals(a.ElementType, b.ElementType),
			(IlPointerTypeRef a, IlPointerTypeRef b) => TypeEquals(a.ElementType, b.ElementType),
			(IlByRefTypeRef a, IlByRefTypeRef b) => TypeEquals(a.ElementType, b.ElementType),
			(IlPinnedTypeRef a, IlPinnedTypeRef b) => TypeEquals(a.ElementType, b.ElementType),
			(IlModifiedTypeRef a, IlModifiedTypeRef b) =>
				a.IsRequired == b.IsRequired && TypeEquals(a.Modifier, b.Modifier) &&
				TypeEquals(a.UnmodifiedType, b.UnmodifiedType),
			(IlFunctionPointerTypeRef a, IlFunctionPointerTypeRef b) => SignatureEquals(a.Signature, b.Signature),
			(IlGlobalModuleTypeRef a, IlGlobalModuleTypeRef b) => ScopeEquals(a.Scope, b.Scope),
			_ => false,
		};
	}

	internal static int TypeHashCode(IlTypeRef value) {
		InternalStateException.ThrowIfNull(value);
		HashCode hash = new();
		hash.Add(value.GetType());
		switch (value) {
		case IlPrimitiveTypeRef type:
			hash.Add(type.Code);
			break;
		case IlNamedTypeRef type:
			hash.Add(ScopeHashCode(type.Scope));
			hash.Add(type.DeclaringType is null ? 0 : TypeHashCode(type.DeclaringType));
			hash.Add(type.Namespace, StringComparer.Ordinal);
			hash.Add(type.Name, StringComparer.Ordinal);
			hash.Add(type.GenericArity);
			hash.Add(type.TypeKind);
			break;
		case IlGenericParameterTypeRef type:
			hash.Add(type.Kind);
			hash.Add(type.Index);
			break;
		case IlGenericInstanceTypeRef type:
			hash.Add(TypeHashCode(type.GenericType));
			foreach (IlTypeRef t in type.Arguments)
				hash.Add(TypeHashCode(t));
			break;
		case IlArrayTypeRef type:
			hash.Add(TypeHashCode(type.ElementType));
			hash.Add(type.Rank);
			foreach (int size in type.Sizes)
				hash.Add(size);
			foreach (int lowerBound in type.LowerBounds)
				hash.Add(lowerBound);
			break;
		case IlSzArrayTypeRef type:
			hash.Add(TypeHashCode(type.ElementType));
			break;
		case IlPointerTypeRef type:
			hash.Add(TypeHashCode(type.ElementType));
			break;
		case IlByRefTypeRef type:
			hash.Add(TypeHashCode(type.ElementType));
			break;
		case IlPinnedTypeRef type:
			hash.Add(TypeHashCode(type.ElementType));
			break;
		case IlModifiedTypeRef type:
			hash.Add(TypeHashCode(type.Modifier));
			hash.Add(TypeHashCode(type.UnmodifiedType));
			hash.Add(type.IsRequired);
			break;
		case IlFunctionPointerTypeRef type:
			hash.Add(SignatureHashCode(type.Signature));
			break;
		case IlGlobalModuleTypeRef type:
			hash.Add(ScopeHashCode(type.Scope));
			break;
		default:
			throw new InternalStateException($"unknown IlTypeRef derived type '{value.GetType()}'");
		}
		return hash.ToHashCode();
	}

	internal static bool ScopeEquals(IlTypeScope? left, IlTypeScope? right) {
		if (ReferenceEquals(left, right))
			return true;
		if (left is null || right is null || left.GetType() != right.GetType())
			return false;
		return (left, right) switch {
			(IlTypeScope.Module a, IlTypeScope.Module b) => a.Identity == b.Identity,
			(IlTypeScope.ModuleReference a, IlTypeScope.ModuleReference b) => StringComparer.OrdinalIgnoreCase.Equals(a.Name, b.Name),
			(IlTypeScope.Assembly a, IlTypeScope.Assembly b) => a.Identity == b.Identity,
			_ => false,
		};
	}

	internal static int ScopeHashCode(IlTypeScope value) {
		InternalStateException.ThrowIfNull(value);
		HashCode hash = new();
		hash.Add(value.GetType());
		switch (value) {
		case IlTypeScope.Module module:
			hash.Add(module.Identity);
			break;
		case IlTypeScope.ModuleReference moduleReference:
			hash.Add(moduleReference.Name, StringComparer.OrdinalIgnoreCase);
			break;
		case IlTypeScope.Assembly assembly:
			hash.Add(assembly.Identity);
			break;
		default:
			throw new InternalStateException($"unknown IlTypeScope derived type '{value.GetType()}'");
		}
		return hash.ToHashCode();
	}

	internal static bool SignatureEquals(IlMethodSignature? left, IlMethodSignature? right) {
		if (ReferenceEquals(left, right))
			return true;
		if (left is null || right is null)
			return false;
		return left.CallingConvention == right.CallingConvention &&
			left.HasThis == right.HasThis && left.ExplicitThis == right.ExplicitThis &&
			left.GenericParameterCount == right.GenericParameterCount &&
			left.RequiredParameterCount == right.RequiredParameterCount &&
			TypeEquals(left.ReturnType, right.ReturnType) &&
			typeSequenceEquals(left.ParameterTypes, right.ParameterTypes);
	}

	internal static int SignatureHashCode(IlMethodSignature value) {
		InternalStateException.ThrowIfNull(value);
		HashCode hash = new();
		hash.Add(value.CallingConvention);
		hash.Add(value.HasThis);
		hash.Add(value.ExplicitThis);
		hash.Add(value.GenericParameterCount);
		hash.Add(value.RequiredParameterCount);
		hash.Add(TypeHashCode(value.ReturnType));
		foreach (IlTypeRef t in value.ParameterTypes)
			hash.Add(TypeHashCode(t));
		return hash.ToHashCode();
	}

	internal static bool MethodEquals(IlMethodRef? left, IlMethodRef? right) =>
		ReferenceEquals(left, right) || left is not null && right is not null &&
		left.Name == right.Name &&
		TypeEquals(left.DeclaringType, right.DeclaringType) &&
		SignatureEquals(left.Signature, right.Signature) &&
		typeSequenceEquals(left.GenericArguments, right.GenericArguments);

	internal static int MethodHashCode(IlMethodRef value) {
		InternalStateException.ThrowIfNull(value);
		HashCode hash = new();
		hash.Add(value.Name, StringComparer.Ordinal);
		hash.Add(TypeHashCode(value.DeclaringType));
		hash.Add(SignatureHashCode(value.Signature));
		foreach (IlTypeRef t in value.GenericArguments)
			hash.Add(TypeHashCode(t));
		return hash.ToHashCode();
	}

	internal static bool FieldEquals(IlFieldRef? left, IlFieldRef? right) =>
		ReferenceEquals(left, right) || left is not null && right is not null &&
		left.Name == right.Name &&
		TypeEquals(left.DeclaringType, right.DeclaringType) &&
		TypeEquals(left.FieldType, right.FieldType);

	internal static int FieldHashCode(IlFieldRef value) {
		InternalStateException.ThrowIfNull(value);
		HashCode hash = new();
		hash.Add(value.Name, StringComparer.Ordinal);
		hash.Add(TypeHashCode(value.DeclaringType));
		hash.Add(TypeHashCode(value.FieldType));
		return hash.ToHashCode();
	}

	private static bool typeSequenceEquals(ImmutableArray<IlTypeRef> left, ImmutableArray<IlTypeRef> right) {
		if (left.Length != right.Length)
			return false;
		for (int i = 0; i < left.Length; i++)
			if (!TypeEquals(left[i], right[i]))
				return false;
		return true;
	}
}
