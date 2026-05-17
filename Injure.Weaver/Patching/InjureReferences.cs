// SPDX-License-Identifier: MIT

using System;
using Mono.Cecil;

namespace Injure.Weaver.Patching;

public sealed class InjureReferences {
	public required TypeReference PublicizedAttributeType { get; init; }
	public required MethodReference PublicizedAttributeCtor { get; init; }

	public required TypeReference ModHookTargetStoreAttributeType { get; init; }
	public required MethodReference ModHookTargetStoreAttributeCtor { get; init; }

	public required TypeReference HookTargetType { get; init; }
	public required MethodReference HookTargetCtor { get; init; }
}

public static class InjureReferenceResolver {
	public static InjureReferences Resolve(ModuleDefinition module) {
		TypeDefinition publicizedAttribute = findRequiredType(module, "Injure.ModKit.Abstractions.PublicizedAttribute");
		MethodDefinition publicizedCtor = findCtor(publicizedAttribute, static ctor =>
			ctor.Parameters.Count == 0
		);

		TypeDefinition storeAttribute = findRequiredType(module, "Injure.ModKit.Abstractions.MonoMod.ModHookTargetStoreAttribute");
		MethodDefinition storeAttributeCtor = findCtor(storeAttribute, static ctor =>
			ctor.Parameters.Count == 1 && ctor.Parameters[0].ParameterType.FullName == "System.Type"
		);

		TypeDefinition hookTarget = findRequiredType(module, "Injure.ModKit.Abstractions.MonoMod.HookTarget");
		MethodDefinition hookTargetCtor = findCtor(hookTarget, static ctor =>
			ctor.Parameters.Count == 3 &&
			ctor.Parameters[0].ParameterType.FullName == "System.String" &&
			ctor.Parameters[1].ParameterType.FullName == "System.Reflection.MethodBase" &&
			ctor.Parameters[2].ParameterType.FullName == "System.Type"
		);

		return new InjureReferences {
			PublicizedAttributeType = module.ImportReference(publicizedAttribute),
			PublicizedAttributeCtor = module.ImportReference(publicizedCtor),
			ModHookTargetStoreAttributeType = module.ImportReference(storeAttribute),
			ModHookTargetStoreAttributeCtor = module.ImportReference(storeAttributeCtor),
			HookTargetType = module.ImportReference(hookTarget),
			HookTargetCtor = module.ImportReference(hookTargetCtor),
		};
	}

	private static TypeDefinition findRequiredType(ModuleDefinition module, string typeFullName) {
		foreach (AssemblyNameReference reference in module.AssemblyReferences) {
			AssemblyDefinition? assembly;

			try {
				assembly = module.AssemblyResolver.Resolve(reference);
			} catch {
				continue;
			}

			foreach (TypeDefinition type in assembly.MainModule.Types) {
				TypeDefinition? found = findTypeRecursive(type, typeFullName);
				if (found is not null)
					return found;
			}
		}

		foreach (TypeDefinition type in module.Types) {
			TypeDefinition? found = findTypeRecursive(type, typeFullName);
			if (found is not null)
				return found;
		}

		throw new InvalidOperationException($"failed to find type '{typeFullName}'; does the input assembly not reference Injure?");
	}

	private static TypeDefinition? findTypeRecursive(TypeDefinition type, string typeFullName) {
		if (type.FullName == typeFullName)
			return type;

		foreach (TypeDefinition nested in type.NestedTypes) {
			TypeDefinition? found = findTypeRecursive(nested, typeFullName);
			if (found is not null)
				return found;
		}

		return null;
	}

	private static MethodDefinition findCtor(TypeDefinition type, Func<MethodDefinition, bool> predicate) {
		foreach (MethodDefinition method in type.Methods) {
			if (!method.IsConstructor)
				continue;
			if (predicate(method))
				return method;
		}
		throw new InvalidOperationException($"'{type.FullName}' unexpectedly doesn't provide a constructor we need");
	}
}
