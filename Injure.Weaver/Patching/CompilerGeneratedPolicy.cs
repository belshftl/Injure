// SPDX-License-Identifier: MIT

using System;
using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class CompilerGeneratedPolicy {
	public static bool HasCompilerGeneratedAttribute(ICustomAttributeProvider provider) {
		foreach (CustomAttribute attribute in provider.CustomAttributes)
			if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
				return true;
		return false;
	}

	public static bool NameLooksCompilerGenerated(string methodName) =>
		methodName.Contains('<', StringComparison.Ordinal) || methodName.Contains('>', StringComparison.Ordinal);

	public static bool LooksCompilerGenerated(TypeDefinition type) =>
		HasCompilerGeneratedAttribute(type) || NameLooksCompilerGenerated(type.Name);
	public static bool LooksCompilerGenerated(FieldDefinition field) =>
		HasCompilerGeneratedAttribute(field) || NameLooksCompilerGenerated(field.Name);
	public static bool LooksCompilerGenerated(MethodDefinition method) =>
		HasCompilerGeneratedAttribute(method) || NameLooksCompilerGenerated(method.Name);
}
