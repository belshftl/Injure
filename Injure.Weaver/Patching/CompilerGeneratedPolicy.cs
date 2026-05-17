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
		methodName.Contains('<') || methodName.Contains('>') || methodName.Contains("__", StringComparison.Ordinal);
}
