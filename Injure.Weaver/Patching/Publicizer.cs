// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;

using Injure.Weaver.Model;

namespace Injure.Weaver.Patching;

public static class Publicizer {
	public static void Publicize(ModuleDefinition module, in AssemblyAnalysis analysis) {
		foreach (TypeDefinition type in module.Types)
			publicizeTypeRecursive(type, in analysis);
	}

	private static void publicizeTypeRecursive(TypeDefinition type, in AssemblyAnalysis analysis) {
		publicizeType(type, in analysis);
		foreach (FieldDefinition field in type.Fields)
			publicizeField(field, in analysis);
		foreach (MethodDefinition method in type.Methods)
			publicizeMethod(method, in analysis);
		foreach (TypeDefinition nested in type.NestedTypes)
			publicizeTypeRecursive(nested, in analysis);
	}

	private static void publicizeType(TypeDefinition type, in AssemblyAnalysis analysis) {
		if (PublicizationPolicy.ShouldPublicizeType(type, in analysis) == PublicizationKind.No)
			return;
		type.Attributes &= ~TypeAttributes.VisibilityMask;
		type.Attributes |= type.IsNested ? TypeAttributes.NestedPublic : TypeAttributes.Public;
	}

	private static void publicizeField(FieldDefinition field, in AssemblyAnalysis analysis) {
		if (PublicizationPolicy.ShouldPublicizeField(field, in analysis) == PublicizationKind.No)
			return;
		field.Attributes &= ~FieldAttributes.FieldAccessMask;
		field.Attributes |= FieldAttributes.Public;
	}

	private static void publicizeMethod(MethodDefinition method, in AssemblyAnalysis analysis) {
		if (PublicizationPolicy.ShouldPublicizeMethod(method, in analysis) == PublicizationKind.No)
			return;
		method.Attributes &= ~MethodAttributes.MemberAccessMask;
		method.Attributes |= MethodAttributes.Public;
	}
}
