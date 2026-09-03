// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using AsmResolver.DotNet;

namespace Injure.Weaver;

public static class Cleaner {
	public static int Clean(ModuleDefinition module) {
		if (ReadRootSegment(module) is not string rootSegment)
			throw new PatchException("input is not marked with [ModifInjected], so there is nothing to clean");

		List<TypeDefinition> remove = new();
		foreach (TypeDefinition type in module.TopLevelTypes) {
			string ns = type.Namespace?.Value ?? "";
			if (ns == rootSegment || ns.EndsWith("." + rootSegment, StringComparison.Ordinal))
				remove.Add(type);
		}
		foreach (TypeDefinition type in remove)
			module.TopLevelTypes.Remove(type);

		if (module.Assembly is AssemblyDefinition asm)
			for (int i = asm.CustomAttributes.Count - 1; i >= 0; i--)
				if (asm.CustomAttributes[i].Constructor?.DeclaringType?.Name?.Value == "ModifInjectedAttribute")
					asm.CustomAttributes.RemoveAt(i);

		return remove.Count;
	}

	public static string? ReadRootSegment(ModuleDefinition module) {
		if (module.Assembly is not AssemblyDefinition asm)
			return null;
		foreach (CustomAttribute attr in asm.CustomAttributes) {
			if (attr.Constructor?.DeclaringType?.Name?.Value != "ModifInjectedAttribute")
				continue;
			if (attr.Signature is not null && attr.Signature.FixedArguments.Count > 0)
				return attr.Signature.FixedArguments[0].Element as AsmResolver.Utf8String;
		}
		return null;
	}
}
