// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

using Mono.Cecil;

namespace Injure.Weaver.Patching;

public sealed class InjureReferences {
	public const string PublicizedAttributeFullName = "Injure.Mods.Weaver.PublicizedAttribute";
	public const string PublicizedSignatureAttributeFullName = "Injure.Mods.Weaver.PublicizedSignatureAttribute";
	public const string PublicizedStateMachineAttributeFullName = "Injure.Mods.Weaver.PublicizedStateMachineAttribute";
	public const string ModHookTargetStoreAttributeFullName = "Injure.Mods.MonoMod.ModHookTargetStoreAttribute";
	public const string HookTargetFullName = "Injure.Mods.MonoMod.HookTarget";

	public required TypeReference PublicizedAttributeType { get; init; }
	public required MethodReference PublicizedAttributeCtor { get; init; }

	public required TypeReference PublicizedSignatureAttributeType { get; init; }
	public required MethodReference PublicizedSignatureAttributeCtor { get; init; }

	public required TypeReference PublicizedStateMachineAttributeType { get; init; }
	public required MethodReference PublicizedStateMachineAttributeCtor { get; init; }

	public required TypeReference ModHookTargetStoreAttributeType { get; init; }
	public required MethodReference ModHookTargetStoreAttributeCtor { get; init; }

	public required TypeReference HookTargetType { get; init; }
	public required MethodReference HookTargetCtor { get; init; }
}

public static class InjureReferenceResolver {
	public static InjureReferences Resolve(ModuleDefinition module) {
		TypeDefinition publicizedAttribute = findRequiredType(module, InjureReferences.PublicizedAttributeFullName);
		MethodDefinition publicizedCtor = findCtor(
			publicizedAttribute,
			static ctor =>
				ctor.Parameters.Count == 0
		);

		TypeDefinition publicizedSignatureAttribute = findRequiredType(module, InjureReferences.PublicizedSignatureAttributeFullName);
		MethodDefinition publicizedSignatureCtor = findCtor(
			publicizedSignatureAttribute,
			static ctor =>
				ctor.Parameters.Count == 0
		);

		TypeDefinition publicizedStateMachineAttribute = findRequiredType(module, InjureReferences.PublicizedStateMachineAttributeFullName);
		MethodDefinition publicizedStateMachineCtor = findCtor(
			publicizedStateMachineAttribute,
			static ctor =>
				ctor.Parameters.Count == 0
		);

		TypeDefinition storeAttribute = findRequiredType(module, InjureReferences.ModHookTargetStoreAttributeFullName);
		MethodDefinition storeAttributeCtor = findCtor(
			storeAttribute,
			static ctor =>
				ctor.Parameters.Count == 1 && ctor.Parameters[0].ParameterType.FullName == "System.Type"
		);

		TypeDefinition hookTarget = findRequiredType(module, InjureReferences.HookTargetFullName);
		MethodDefinition hookTargetCtor = findCtor(
			hookTarget,
			static ctor =>
				ctor.Parameters.Count == 3 &&
				ctor.Parameters[0].ParameterType.FullName == "System.String" &&
				ctor.Parameters[1].ParameterType.FullName == "System.Reflection.MethodBase" &&
				ctor.Parameters[2].ParameterType.FullName == "System.Type"
		);

		return new InjureReferences {
			PublicizedAttributeType = module.ImportReference(publicizedAttribute),
			PublicizedAttributeCtor = module.ImportReference(publicizedCtor),
			PublicizedSignatureAttributeType = module.ImportReference(publicizedSignatureAttribute),
			PublicizedSignatureAttributeCtor = module.ImportReference(publicizedSignatureCtor),
			PublicizedStateMachineAttributeType = module.ImportReference(publicizedStateMachineAttribute),
			PublicizedStateMachineAttributeCtor = module.ImportReference(publicizedStateMachineCtor),
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
