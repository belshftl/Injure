// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Weaver.Model;
using Mono.Cecil;

namespace Injure.Weaver.Patching;

public static class TargetDiscoverer {
	public static List<TargetCandidate> Discover(ModuleDefinition module, string ownerID) {
		List<TargetCandidate> result = new();
		Dictionary<string, int> nameCounts = new(StringComparer.Ordinal);
		foreach (TypeDefinition type in module.Types)
			discoverTypeRecursive(ownerID, type, result, nameCounts);
		return result;
	}

	private static void discoverTypeRecursive(string ownerID, TypeDefinition type, List<TargetCandidate> result, Dictionary<string, int> nameCounts) {
		if (GeneratedCodePolicy.LooksGenerated(type))
			return;

		foreach (MethodDefinition method in type.Methods) {
			if (!isSupported(method))
				continue;
			result.Add(createCandidate(ownerID, method, nameCounts));
		}

		foreach (TypeDefinition nested in type.NestedTypes)
			discoverTypeRecursive(ownerID, nested, result, nameCounts);
	}

	private static bool isSupported(MethodDefinition method) {
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

	private static TargetCandidate createCandidate(string ownerID, MethodDefinition method, Dictionary<string, int> nameCounts) {
		const string prefix = "targets";
		string stamp = SignatureHasher.Hash(method)[..8];
		string container = TypeNameUtil.GetContainerName(method.DeclaringType);
		string methodBase = TypeNameUtil.GetMethodBaseName(method);
		string localID = $"{container}/{methodBase}#{stamp}";
		string id = $"{ownerID}::{prefix}/{localID}";

		string counterKey = container + ":" + methodBase;
		nameCounts.TryGetValue(counterKey, out int count);
		nameCounts[counterKey] = count + 1;

		string suffix = count == 0 ? "" : "_" + stamp;

		return new TargetCandidate {
			ID = id,
			Method = method,
			ContainerName = container,
			ConstantName = TypeNameUtil.SanitizeIdentifier(methodBase + suffix),
			NextDelegateName = TypeNameUtil.SanitizeIdentifier("next_" + methodBase + suffix),
		};
	}
}
