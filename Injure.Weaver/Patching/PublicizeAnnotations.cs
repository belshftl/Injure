// SPDX-License-Identifier: MIT

using Mono.Cecil;

using Injure.Weaver.Model;

namespace Injure.Weaver.Patching;

public static class PublicizeAnnotations {
	public static void Annotate(ModuleDefinition module, InjureReferences ij, in AssemblyAnalysis analysis) {
		foreach (TypeDefinition type in module.Types)
			annotateTypeRecursive(type, ij, in analysis);
	}

	private static void annotateTypeRecursive(TypeDefinition type, InjureReferences ij, in AssemblyAnalysis analysis) {
		if (!VisibilityUtil.IsPublic(type))
			addAttributes(type, ij, PublicizationPolicy.ShouldPublicizeType(type, in analysis));
		foreach (FieldDefinition field in type.Fields)
			if (!VisibilityUtil.IsPublic(field))
				addAttributes(field, ij, PublicizationPolicy.ShouldPublicizeField(field, in analysis));
		foreach (MethodDefinition method in type.Methods) {
			if (!VisibilityUtil.IsPublic(method))
				addAttributes(method, ij, PublicizationPolicy.ShouldPublicizeMethod(method, in analysis));
			if (VisibilityUtil.SignatureMentionsOriginallyNonPublicType(method))
				addPublicizedSignatureAttribute(method, ij);
		}
		foreach (TypeDefinition nested in type.NestedTypes)
			annotateTypeRecursive(nested, ij, in analysis);
	}

	private static void addAttributes(ICustomAttributeProvider provider, InjureReferences ij, PublicizationKind kind) {
		switch (kind) {
		case PublicizationKind.No:
			break;
		case PublicizationKind.Normal:
			addPublicizedAttribute(provider, ij);
			break;
		case PublicizationKind.StateMachine:
			addPublicizedAttribute(provider, ij);
			addPublicizedStateMachineAttribute(provider, ij);
			break;
		}
	}

	private static void addPublicizedAttribute(ICustomAttributeProvider provider, InjureReferences ij) {
		foreach (CustomAttribute existing in provider.CustomAttributes)
			if (existing.AttributeType.FullName == InjureReferences.PublicizedAttributeFullName)
				return;
		CustomAttribute attribute = new(ij.PublicizedAttributeCtor);
		provider.CustomAttributes.Add(attribute);
	}

	private static void addPublicizedSignatureAttribute(MethodDefinition method, InjureReferences ij) {
		foreach (CustomAttribute existing in method.CustomAttributes)
			if (existing.AttributeType.Name == InjureReferences.PublicizedSignatureAttributeFullName)
				return;
		CustomAttribute attribute = new(ij.PublicizedSignatureAttributeCtor);
		method.CustomAttributes.Add(attribute);
	}

	private static void addPublicizedStateMachineAttribute(ICustomAttributeProvider provider, InjureReferences ij) {
		foreach (CustomAttribute existing in provider.CustomAttributes)
			if (existing.AttributeType.Name == InjureReferences.PublicizedStateMachineAttributeFullName)
				return;
		CustomAttribute attribute = new(ij.PublicizedStateMachineAttributeCtor);
		provider.CustomAttributes.Add(attribute);
	}
}
