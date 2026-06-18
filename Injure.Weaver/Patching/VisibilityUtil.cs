// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class VisibilityUtil {
	public static string GetTypeAccessibility(TypeDefinition type) {
		if (!type.IsNested)
			return type.IsPublic ? "public" : "internal";
		if (type.IsNestedPublic)
			return "public";
		if (type.IsNestedFamily)
			return "protected";
		if (type.IsNestedAssembly)
			return "internal";
		if (type.IsNestedFamilyOrAssembly)
			return "protected internal";
		if (type.IsNestedFamilyAndAssembly)
			return "private protected";
		return "private";
	}

	public static bool IsPublic(TypeDefinition type) => type.IsNested ? type.IsNestedPublic : type.IsPublic;
	public static bool IsPublic(MethodDefinition method) => method.IsPublic;
	public static bool IsPublic(FieldDefinition field) => field.IsPublic;

	public static bool IsOriginallyNonPublicType(TypeReference type) {
		TypeReference normalized = Unwrap(type);
		if (normalized is GenericParameter)
			return false;
		try {
			TypeDefinition? definition = normalized.Resolve();
			if (definition is null)
				return false;
			if (definition.Module.Assembly != type.Module.Assembly)
				return false;
			return !IsPublic(definition);
		} catch {
			return false;
		}
	}

	public static bool SignatureMentionsOriginallyNonPublicType(MethodDefinition method) {
		if (TypeMentionsOriginallyNonPublicType(method.ReturnType))
			return true;
		foreach (ParameterDefinition parameter in method.Parameters)
			if (TypeMentionsOriginallyNonPublicType(parameter.ParameterType))
				return true;
		foreach (GenericParameter parameter in method.GenericParameters)
			foreach (GenericParameterConstraint constraint in parameter.Constraints)
				if (TypeMentionsOriginallyNonPublicType(constraint.ConstraintType))
					return true;
		return false;
	}

	public static bool TypeMentionsOriginallyNonPublicType(TypeReference type) {
		TypeReference normalized = Unwrap(type);
		if (IsOriginallyNonPublicType(normalized))
			return true;
		if (normalized is GenericInstanceType generic)
			foreach (TypeReference argument in generic.GenericArguments)
				if (TypeMentionsOriginallyNonPublicType(argument))
					return true;
		return false;
	}

	public static TypeReference Unwrap(TypeReference type) {
		for (;;) {
			switch (type) {
			case ByReferenceType byRef:
				type = byRef.ElementType;
				continue;
			case PointerType pointer:
				type = pointer.ElementType;
				continue;
			case ArrayType array:
				type = array.ElementType;
				continue;
			case OptionalModifierType optional:
				type = optional.ElementType;
				continue;
			case RequiredModifierType required:
				type = required.ElementType;
				continue;
			}
			return type;
		}
	}
}
