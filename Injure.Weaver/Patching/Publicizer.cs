// SPDX-License-Identifier: MIT

using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class Publicizer {
	public static void Publicize(ModuleDefinition module) {
		foreach (TypeDefinition type in module.Types)
			publicizeTypeRecursive(type);
	}

	private static void publicizeTypeRecursive(TypeDefinition type) {
		if (CompilerGeneratedPolicy.HasCompilerGeneratedAttribute(type) || CompilerGeneratedPolicy.NameLooksCompilerGenerated(type.Name))
			return;
		publicizeType(type);
		foreach (FieldDefinition field in type.Fields)
			publicizeField(field);
		foreach (MethodDefinition method in type.Methods)
			publicizeMethod(method);
		foreach (TypeDefinition nested in type.NestedTypes)
			publicizeTypeRecursive(nested);
	}

	private static void publicizeType(TypeDefinition type) {
		if (CompilerGeneratedPolicy.HasCompilerGeneratedAttribute(type) || CompilerGeneratedPolicy.NameLooksCompilerGenerated(type.Name))
			return;
		type.Attributes &= ~TypeAttributes.VisibilityMask;
		type.Attributes |= type.IsNested ? TypeAttributes.NestedPublic : TypeAttributes.Public;
	}

	private static void publicizeField(FieldDefinition field) {
		if (CompilerGeneratedPolicy.HasCompilerGeneratedAttribute(field) || CompilerGeneratedPolicy.NameLooksCompilerGenerated(field.Name))
			return;
		field.Attributes &= ~FieldAttributes.FieldAccessMask;
		field.Attributes |= FieldAttributes.Public;
	}

	private static void publicizeMethod(MethodDefinition method) {
		if (CompilerGeneratedPolicy.HasCompilerGeneratedAttribute(method) || CompilerGeneratedPolicy.NameLooksCompilerGenerated(method.Name))
			return;
		if (method.IsRuntimeSpecialName && method.Name == ".cctor")
			return;
		method.Attributes &= ~MethodAttributes.MemberAccessMask;
		method.Attributes |= MethodAttributes.Public;
	}
}
