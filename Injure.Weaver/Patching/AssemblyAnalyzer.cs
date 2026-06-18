// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using Mono.Cecil;

using Injure.Weaver.Model;

namespace Injure.Weaver.Patching;

public static class AssemblyAnalyzer {
	public static AssemblyAnalysis Analyze(ModuleDefinition module) {
		List<TypeDefinition> types = getAllTypes(module);

		HashSet<string> originallyNonPublic = new(StringComparer.Ordinal);
		foreach (TypeDefinition type in types)
			if (!VisibilityUtil.IsPublic(type))
				originallyNonPublic.Add(type.FullName);

		Dictionary<string, PublicizedStateMachineKind> stateMachines = StateMachineDiscovery.Discover(types);

		return new AssemblyAnalysis {
			OriginallyNonPublicTypeFullNames = originallyNonPublic.ToFrozenSet(StringComparer.Ordinal),
			StateMachineTypeFullNames = stateMachines.ToFrozenDictionary(StringComparer.Ordinal),
		};
	}

	private static List<TypeDefinition> getAllTypes(ModuleDefinition module) {
		List<TypeDefinition> result = new();
		foreach (TypeDefinition type in module.Types)
			addType(type, result);
		return result;
	}

	private static void addType(TypeDefinition type, List<TypeDefinition> result) {
		result.Add(type);
		foreach (TypeDefinition nested in type.NestedTypes)
			addType(nested, result);
	}
}
