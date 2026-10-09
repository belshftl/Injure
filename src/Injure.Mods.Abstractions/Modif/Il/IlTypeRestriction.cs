// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Mods.Abstractions.Modif.Il;

internal enum IlTypeRestriction {
	MemberOfReloadableMod,
	TypeTokenOfReloadableMod,
	ValueTypeLayoutRequired,
}

/// <summary>
/// Checks for the three restrictions imposed on types from reloadable mods.
/// </summary>
/// <remarks>
/// See <see cref="IlCollectibleReferenceException"/>'s type docs for more info. The distinction
/// between members and type tokens is made here for completeness, even though they have the
/// same restrictions in practice.
/// </remarks>
internal static class IlTypeRestrictionCheck {
	public static void AssertUnrestricted(string what, IlTypeRestriction? restriction) {
		switch (restriction) {
		case IlTypeRestriction.MemberOfReloadableMod:
			throw new IlCollectibleReferenceException($"{what} would name a member of a reloadable mod");
		case IlTypeRestriction.TypeTokenOfReloadableMod:
			throw new IlCollectibleReferenceException($"{what} would name a type of a reloadable mod");
		case IlTypeRestriction.ValueTypeLayoutRequired:
			throw new IlCollectibleReferenceException($"{what} would require the JIT to resolve the layout of a value type from a reloadable mod; pass it as a byref/pointer or use a reference type");
		}
	}

	public static IlTypeRestriction? CheckMethodOperand(IlMethodRef method, in IlOwnerInfo info) {
		InternalStateException.ThrowIfNull(method);
		if (isReloadable(method.DeclaringType, in info))
			return IlTypeRestriction.MemberOfReloadableMod;
		if (CheckSignature(method.Signature, in info) is IlTypeRestriction r)
			return r;
		foreach (IlTypeRef arg in method.GenericArguments)
			if (CheckSignatureType(arg, in info) is IlTypeRestriction fromArg)
				return fromArg;
		return null;
	}

	public static IlTypeRestriction? CheckFieldOperand(IlFieldRef field, in IlOwnerInfo info) {
		InternalStateException.ThrowIfNull(field);
		if (isReloadable(field.DeclaringType, in info))
			return IlTypeRestriction.MemberOfReloadableMod;
		return CheckSignatureType(field.FieldType, in info);
	}

	public static IlTypeRestriction? CheckSignature(IlMethodSignature signature, in IlOwnerInfo info) {
		InternalStateException.ThrowIfNull(signature);
		if (CheckSignatureType(signature.ReturnType, in info) is IlTypeRestriction fromReturn)
			return fromReturn;
		foreach (IlTypeRef param in signature.ParameterTypes)
			if (CheckSignatureType(param, in info) is IlTypeRestriction fromParam)
				return fromParam;
		return null;
	}

	public static IlTypeRestriction? CheckLocals(ImmutableArray<IlTypeRef> locals, in IlOwnerInfo info) {
		if (locals.IsDefaultOrEmpty)
			return null;
		foreach (IlTypeRef local in locals)
			if (CheckSignatureType(local, in info) is IlTypeRestriction r)
				return r;
		return null;
	}

	public static IlTypeRestriction? CheckSignatureType(IlTypeRef type, in IlOwnerInfo info) {
		InternalStateException.ThrowIfNull(type);
		switch (type) {
		case IlNamedTypeRef named:
			return named.TypeKind == IlNamedTypeKind.ValueType && isReloadable(named, in info)
				? IlTypeRestriction.ValueTypeLayoutRequired
				: null;
		case IlGenericInstanceTypeRef genericInst: {
			if (CheckSignatureType(genericInst.GenericType, in info) is IlTypeRestriction fromDef)
				return fromDef;
			if (genericInst.GenericType.TypeKind != IlNamedTypeKind.ValueType)
				return null;
			foreach (IlTypeRef arg in genericInst.Arguments)
				if (CheckSignatureType(arg, in info) is IlTypeRestriction fromArg)
					return fromArg;
			return null;
		}
		case IlPinnedTypeRef pinned:
			return CheckSignatureType(pinned.PinnedType, in info);
		case IlModifiedTypeRef modified:
			return CheckSignatureType(modified.UnmodifiedType, in info);
		default:
			return null;
		}
	}

	public static IlTypeRestriction? CheckTypeOperand(IlTypeRef type, in IlOwnerInfo info) {
		InternalStateException.ThrowIfNull(type);
		switch (type) {
		case IlNamedTypeRef named:
			return isReloadable(named, in info) ? IlTypeRestriction.TypeTokenOfReloadableMod : null;
		case IlGenericInstanceTypeRef genericInst: {
			if (CheckTypeOperand(genericInst.GenericType, in info) is IlTypeRestriction fromDef)
				return fromDef;
			foreach (IlTypeRef arg in genericInst.Arguments)
				if (CheckTypeOperand(arg, in info) is IlTypeRestriction fromArg)
					return fromArg;
			return null;
		}
		case IlArrayTypeRef array:
			return CheckTypeOperand(array.ElementType, in info);
		case IlSzArrayTypeRef szArray:
			return CheckTypeOperand(szArray.ElementType, in info);
		case IlPointerTypeRef pointer:
			return CheckTypeOperand(pointer.ElementType, in info);
		case IlByRefTypeRef byRef:
			return CheckTypeOperand(byRef.ElementType, in info);
		case IlPinnedTypeRef pinned:
			return CheckTypeOperand(pinned.PinnedType, in info);
		case IlModifiedTypeRef modified:
			return CheckTypeOperand(modified.Modifier, in info) ?? CheckTypeOperand(modified.UnmodifiedType, in info);
		default:
			return null;
		}
	}

	private static bool isReloadable(IlTypeRef type, in IlOwnerInfo info) {
		for (;;) {
			switch (type) {
			case IlNamedTypeRef named:
				while (named.DeclaringType is IlNamedTypeRef declaring)
					named = declaring;
				return info.IsReloadable(named.Scope);
			case IlGenericInstanceTypeRef genericInst:
				type = genericInst.GenericType;
				continue;
			case IlArrayTypeRef array:
				type = array.ElementType;
				continue;
			case IlSzArrayTypeRef szArray:
				type = szArray.ElementType;
				continue;
			case IlPointerTypeRef pointer:
				type = pointer.ElementType;
				continue;
			case IlByRefTypeRef byRef:
				type = byRef.ElementType;
				continue;
			case IlPinnedTypeRef pinned:
				type = pinned.PinnedType;
				continue;
			case IlModifiedTypeRef modified:
				type = modified.UnmodifiedType;
				continue;
			default:
				return false;
			}
		}
	}
}
