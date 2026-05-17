// SPDX-License-Identifier: MIT

using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class PrePublicizeAnnotations {
	public static void AnnotateBeforePublicize(ModuleDefinition module, InjureReferences ij) {
		foreach (TypeDefinition type in module.Types)
			annotateTypeRecursive(type, ij);
	}

	private static void annotateTypeRecursive(TypeDefinition type, InjureReferences ij) {
		if (CompilerGeneratedPolicy.HasCompilerGeneratedAttribute(type) || CompilerGeneratedPolicy.NameLooksCompilerGenerated(type.Name))
			return;
		if (!VisibilityUtil.IsPublic(type))
			addPublicizedAttribute(type, ij);
		foreach (FieldDefinition field in type.Fields)
			if (!VisibilityUtil.IsPublic(field) && !CompilerGeneratedPolicy.HasCompilerGeneratedAttribute(field) && !CompilerGeneratedPolicy.NameLooksCompilerGenerated(field.Name))
				addPublicizedAttribute(field, ij);
		foreach (MethodDefinition method in type.Methods)
			if (VisibilityUtil.SignatureMentionsOriginallyNonPublicType(method) && !CompilerGeneratedPolicy.HasCompilerGeneratedAttribute(method) && !CompilerGeneratedPolicy.NameLooksCompilerGenerated(method.Name))
				addPublicizedAttribute(method, ij);
		foreach (TypeDefinition nested in type.NestedTypes)
			annotateTypeRecursive(nested, ij);
	}

	private static void addPublicizedAttribute(ICustomAttributeProvider provider, InjureReferences ij) {
		foreach (CustomAttribute existing in provider.CustomAttributes)
			if (existing.AttributeType.Name == "PublicizedAttribute")
				return;
		CustomAttribute attribute = new(ij.PublicizedAttributeCtor);
		provider.CustomAttributes.Add(attribute);
	}
}
