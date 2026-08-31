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

	public static IlTypeRestriction? CheckMethodOperand(IlMethodRef method, in IlOwnerCtx ctx) {
		InternalStateException.ThrowIfNull(method);
		if (isReloadable(method.DeclaringType, in ctx))
			return IlTypeRestriction.MemberOfReloadableMod;
		if (CheckSignature(method.Signature, in ctx) is IlTypeRestriction r)
			return r;
		foreach (IlTypeRef arg in method.GenericArguments)
			if (CheckSignatureType(arg, in ctx) is IlTypeRestriction fromArg)
				return fromArg;
		return null;
	}

	public static IlTypeRestriction? CheckFieldOperand(IlFieldRef field, in IlOwnerCtx ctx) {
		InternalStateException.ThrowIfNull(field);
		if (isReloadable(field.DeclaringType, in ctx))
			return IlTypeRestriction.MemberOfReloadableMod;
		return CheckSignatureType(field.FieldType, in ctx);
	}

	public static IlTypeRestriction? CheckSignature(IlMethodSignature signature, in IlOwnerCtx ctx) {
		InternalStateException.ThrowIfNull(signature);
		if (CheckSignatureType(signature.ReturnType, in ctx) is IlTypeRestriction fromReturn)
			return fromReturn;
		foreach (IlTypeRef param in signature.ParameterTypes)
			if (CheckSignatureType(param, in ctx) is IlTypeRestriction fromParam)
				return fromParam;
		return null;
	}

	public static IlTypeRestriction? CheckLocals(ImmutableArray<IlTypeRef> locals, in IlOwnerCtx ctx) {
		if (locals.IsDefaultOrEmpty)
			return null;
		foreach (IlTypeRef local in locals)
			if (CheckSignatureType(local, in ctx) is IlTypeRestriction r)
				return r;
		return null;
	}

	public static IlTypeRestriction? CheckSignatureType(IlTypeRef type, in IlOwnerCtx ctx) {
		InternalStateException.ThrowIfNull(type);
		switch (type) {
		case IlNamedTypeRef named:
			return named.TypeKind == IlNamedTypeKind.ValueType && isReloadable(named, in ctx)
				? IlTypeRestriction.ValueTypeLayoutRequired
				: null;
		case IlGenericInstanceTypeRef genericInst: {
			if (CheckSignatureType(genericInst.GenericType, in ctx) is IlTypeRestriction fromDef)
				return fromDef;
			if (genericInst.GenericType.TypeKind != IlNamedTypeKind.ValueType)
				return null;
			foreach (IlTypeRef arg in genericInst.Arguments)
				if (CheckSignatureType(arg, in ctx) is IlTypeRestriction fromArg)
					return fromArg;
			return null;
		}
		case IlPinnedTypeRef pinned:
			return CheckSignatureType(pinned.ElementType, in ctx);
		case IlModifiedTypeRef modified:
			return CheckSignatureType(modified.UnmodifiedType, in ctx);
		default:
			return null;
		}
	}

	public static IlTypeRestriction? CheckTypeOperand(IlTypeRef type, in IlOwnerCtx ctx) {
		InternalStateException.ThrowIfNull(type);
		switch (type) {
		case IlNamedTypeRef named:
			return isReloadable(named, in ctx) ? IlTypeRestriction.TypeTokenOfReloadableMod : null;
		case IlGenericInstanceTypeRef genericInst: {
			if (CheckTypeOperand(genericInst.GenericType, in ctx) is IlTypeRestriction fromDef)
				return fromDef;
			foreach (IlTypeRef arg in genericInst.Arguments)
				if (CheckTypeOperand(arg, in ctx) is IlTypeRestriction fromArg)
					return fromArg;
			return null;
		}
		case IlArrayTypeRef array:
			return CheckTypeOperand(array.ElementType, in ctx);
		case IlSzArrayTypeRef szArray:
			return CheckTypeOperand(szArray.ElementType, in ctx);
		case IlPointerTypeRef pointer:
			return CheckTypeOperand(pointer.ElementType, in ctx);
		case IlByRefTypeRef byRef:
			return CheckTypeOperand(byRef.ElementType, in ctx);
		case IlPinnedTypeRef pinned:
			return CheckTypeOperand(pinned.ElementType, in ctx);
		case IlModifiedTypeRef modified:
			return CheckTypeOperand(modified.Modifier, in ctx) ?? CheckTypeOperand(modified.UnmodifiedType, in ctx);
		default:
			return null;
		}
	}

	private static bool isReloadable(IlTypeRef type, in IlOwnerCtx ctx) {
		for (;;) {
			switch (type) {
			case IlNamedTypeRef named:
				while (named.DeclaringType is IlNamedTypeRef declaring)
					named = declaring;
				return ctx.IsReloadable(named.Scope);
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
				type = pinned.ElementType;
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
