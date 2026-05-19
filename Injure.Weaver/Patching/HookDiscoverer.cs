// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Mono.Cecil;

using Injure.Weaver.Model;

namespace Injure.Weaver.Patching;

public static class HookDiscoverer {
	public static List<HookCandidate> Discover(ModuleDefinition module, string ownerID) {
		List<HookCandidate> result = new();
		Dictionary<string, int> nameCounts = new(StringComparer.Ordinal);
		foreach (TypeDefinition type in module.Types)
			discoverTypeRecursive(ownerID, type, result, nameCounts);
		return result;
	}

	private static void discoverTypeRecursive(string ownerID, TypeDefinition type, List<HookCandidate> result, Dictionary<string, int> nameCounts) {
		if (GeneratedCodePolicy.LooksGenerated(type))
			return;

		foreach (MethodDefinition method in type.Methods) {
			if (!isSupportedHookTarget(method))
				continue;
			if (hasIntendedHookPointAttribute(method))
				result.Add(createCandidate(ownerID, HookKind.Intended, method, nameCounts));
			result.Add(createCandidate(ownerID, HookKind.Raw, method, nameCounts));
		}

		foreach (TypeDefinition nested in type.NestedTypes)
			discoverTypeRecursive(ownerID, nested, result, nameCounts);
	}

	private static bool isSupportedHookTarget(MethodDefinition method) {
		if (method.IsConstructor)
			return false;
		if (method.IsAbstract)
			return false;
		if (method.IsPInvokeImpl)
			return false;
		if (method.HasGenericParameters)
			return false;
		if (method.DeclaringType.HasGenericParameters)
			return false;
		if (method.IsGetter || method.IsSetter || method.IsAddOn || method.IsRemoveOn || method.IsFire)
			return false;
		if (GeneratedCodePolicy.LooksGenerated(method.DeclaringType) || GeneratedCodePolicy.LooksGenerated(method))
			return false;
		return true;
	}

	private static bool hasIntendedHookPointAttribute(MethodDefinition method) {
		foreach (CustomAttribute attribute in method.CustomAttributes)
			if (attribute.AttributeType.Name == "IntendedMonoModHookPointAttribute")
				return true;
		return false;
	}

	private static HookCandidate createCandidate(string ownerID, HookKind kind, MethodDefinition method, Dictionary<string, int> nameCounts) {
		string stamp = SignatureHasher.Hash(method)[..8];
		string container = TypeNameUtil.GetContainerName(method.DeclaringType);
		string methodBase = TypeNameUtil.GetMethodBaseName(method);
		string localID = $"{container}/{methodBase}#{stamp}";
		string prefix = kind == HookKind.Intended ? "hooks" : "raw-hooks";
		string id = $"{ownerID}::{prefix}/{localID}";

		string counterKey = kind + ":" + container + ":" + methodBase;
		nameCounts.TryGetValue(counterKey, out int count);
		nameCounts[counterKey] = count + 1;

		string suffix = count == 0 ? "" : "_" + stamp;

		return new HookCandidate {
			Kind = kind,
			ID = id,
			Method = method,
			ContainerName = container,
			ConstantName = TypeNameUtil.SanitizeIdentifier(methodBase + suffix),
			OrigDelegateName = TypeNameUtil.SanitizeIdentifier("orig_" + methodBase + suffix),
		};
	}
}
