// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Mods.Abstractions.Modif.Il;

// TODO: this is basically identical to IlRefEquality except for, like, two relaxations,
// decide if its existence is justified
internal static class IlRefMatching {
	public static bool OperandEquals(IlOperand left, IlOperand right) {
		if (ReferenceEquals(left, right))
			return true;
		if (left.GetType() != right.GetType())
			return false;
		return (left, right) switch {
			(IlInt32Operand a, IlInt32Operand b) => a.Value == b.Value,
			(IlInt64Operand a, IlInt64Operand b) => a.Value == b.Value,
			(IlFloat32Operand a, IlFloat32Operand b) => a.Value.Equals(b.Value),
			(IlFloat64Operand a, IlFloat64Operand b) => a.Value.Equals(b.Value),
			(IlStringOperand a, IlStringOperand b) => a.Value == b.Value,
			(IlArgumentOperand a, IlArgumentOperand b) => a.Index == b.Index,
			(IlLocalOperand a, IlLocalOperand b) => a.Index == b.Index,
			(IlTypeOperand a, IlTypeOperand b) => typeEquals(a.Type, b.Type),
			(IlMethodOperand a, IlMethodOperand b) => methodEquals(a.Method, b.Method),
			(IlFieldOperand a, IlFieldOperand b) => fieldEquals(a.Field, b.Field),
			(IlCallSiteOperand a, IlCallSiteOperand b) => signatureEquals(a.Signature, b.Signature),
			(IlBranchOperand a, IlBranchOperand b) => a.Target == b.Target,
			(IlSwitchOperand a, IlSwitchOperand b) => a.Targets.AsSpan().SequenceEqual(b.Targets.AsSpan()),
			_ => throw new InternalStateException($"unknown IL operand pair '{left.GetType()}'"),
		};
	}

	private static bool typeEquals(IlTypeRef? left, IlTypeRef? right) {
		if (ReferenceEquals(left, right))
			return true;
		if (left is null || right is null || left.GetType() != right.GetType())
			return false;
		return (left, right) switch {
			(IlPrimitiveTypeRef a, IlPrimitiveTypeRef b) => a.Code == b.Code,
			(IlNamedTypeRef a, IlNamedTypeRef b) =>
				IlRefEquality.ScopeEquals(a.Scope, b.Scope) && typeEquals(a.DeclaringType, b.DeclaringType) &&
				(a.DeclaringType is not null || a.Namespace == b.Namespace) &&
				a.Name == b.Name && a.GenericArity == b.GenericArity &&
				(a.TypeKind == b.TypeKind || a.TypeKind == IlNamedTypeKind.Unknown || b.TypeKind == IlNamedTypeKind.Unknown),
			(IlGenericParameterTypeRef a, IlGenericParameterTypeRef b) => a.Kind == b.Kind && a.Index == b.Index,
			(IlGenericInstanceTypeRef a, IlGenericInstanceTypeRef b) =>
				typeEquals(a.GenericType, b.GenericType) && typeSequenceEquals(a.Arguments, b.Arguments),
			(IlArrayTypeRef a, IlArrayTypeRef b) =>
				typeEquals(a.ElementType, b.ElementType) && a.Rank == b.Rank &&
				a.Sizes.AsSpan().SequenceEqual(b.Sizes.AsSpan()) &&
				a.LowerBounds.AsSpan().SequenceEqual(b.LowerBounds.AsSpan()),
			(IlSzArrayTypeRef a, IlSzArrayTypeRef b) => typeEquals(a.ElementType, b.ElementType),
			(IlPointerTypeRef a, IlPointerTypeRef b) => typeEquals(a.ElementType, b.ElementType),
			(IlByRefTypeRef a, IlByRefTypeRef b) => typeEquals(a.ElementType, b.ElementType),
			(IlPinnedTypeRef a, IlPinnedTypeRef b) => typeEquals(a.ElementType, b.ElementType),
			(IlModifiedTypeRef a, IlModifiedTypeRef b) =>
				a.IsRequired == b.IsRequired && typeEquals(a.Modifier, b.Modifier) &&
				typeEquals(a.UnmodifiedType, b.UnmodifiedType),
			(IlFunctionPointerTypeRef a, IlFunctionPointerTypeRef b) => signatureEquals(a.Signature, b.Signature),
			(IlGlobalModuleTypeRef a, IlGlobalModuleTypeRef b) => IlRefEquality.ScopeEquals(a.Scope, b.Scope),
			_ => throw new InternalStateException($"unknown IL type reference pair '{left.GetType()}'"),
		};
	}

	private static bool signatureEquals(IlMethodSignature? left, IlMethodSignature? right) {
		if (ReferenceEquals(left, right))
			return true;
		if (left is null || right is null)
			return false;
		return left.CallingConvention == right.CallingConvention &&
			left.HasThis == right.HasThis && left.ExplicitThis == right.ExplicitThis &&
			left.GenericParameterCount == right.GenericParameterCount &&
			left.RequiredParameterCount == right.RequiredParameterCount &&
			typeEquals(left.ReturnType, right.ReturnType) &&
			typeSequenceEquals(left.ParameterTypes, right.ParameterTypes);
	}

	private static bool methodEquals(IlMethodRef? left, IlMethodRef? right) =>
		ReferenceEquals(left, right) || left is not null && right is not null &&
		left.Name == right.Name &&
		typeEquals(left.DeclaringType, right.DeclaringType) &&
		signatureEquals(left.Signature, right.Signature) &&
		typeSequenceEquals(left.GenericArguments, right.GenericArguments);

	private static bool fieldEquals(IlFieldRef? left, IlFieldRef? right) =>
		ReferenceEquals(left, right) || left is not null && right is not null &&
		left.Name == right.Name &&
		typeEquals(left.DeclaringType, right.DeclaringType) &&
		typeEquals(left.FieldType, right.FieldType);

	private static bool typeSequenceEquals(ImmutableArray<IlTypeRef> left, ImmutableArray<IlTypeRef> right) {
		if (left.Length != right.Length)
			return false;
		for (int i = 0; i < left.Length; i++)
			if (!typeEquals(left[i], right[i]))
				return false;
		return true;
	}
}
