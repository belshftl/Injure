// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Injure.Weaver;

public static class NameMangler {
	private const int maxTier = 3;

	public static void Assign(List<MirrorScope> roots, Options options) {
		foreach (MirrorScope scope in roots)
			assignScope(scope, options);
	}

	private static void assignScope(MirrorScope scope, Options options) {
		List<NameGroup> all = new();
		Dictionary<NameGroup, MirrorScope> reserved = new();

		foreach (MirrorScope child in scope.Children) {
			NameGroup g = new() {
				MirrorOwner = scope.Source,
				BaseName = MirrorTypeName(child.Source),
				CanonicalKey = child.Source.FullName,
			};
			reserved[g] = child;
			all.Add(g);
		}
		all.AddRange(scope.Groups);

		if (options.OverloadNaming == OverloadNamingMode.AlwaysQualified)
			foreach (NameGroup g in scope.Groups)
				g.Tier = 2;

		resolve(all);

		foreach (NameGroup g in all) {
			if (reserved.TryGetValue(g, out MirrorScope? child))
				child.MirrorName = g.Resolved;
		}

		foreach (MirrorScope child in scope.Children)
			assignScope(child, options);
	}

	private static void resolve(List<NameGroup> groups) {
		for (int i = 0; i <= maxTier; i++) {
			Dictionary<string, List<NameGroup>> claims = new(StringComparer.Ordinal);

			foreach (NameGroup g in groups) {
				g.Resolved = candidate(g, g.Tier);
				foreach (string n in namesOf(g)) {
					if (!claims.TryGetValue(n, out List<NameGroup>? list))
						claims[n] = list = new List<NameGroup>();
					if (!list.Contains(g))
						list.Add(g);
				}
			}

			HashSet<NameGroup> conflict = new();
			foreach ((_, List<NameGroup> list) in claims)
				if (list.Count > 1)
					foreach (NameGroup g in list)
						conflict.Add(g);

			if (conflict.Count == 0) {
				commit(groups);
				return;
			}

			bool promoted = false;
			foreach (NameGroup g in conflict) {
				if (g.Tier < maxTier) {
					g.Tier++;
					promoted = true;
				}
			}
			if (!promoted)
				break;
		}

		throw new PatchException("mangled name collision at tier 3; 64-bit hash collision?");
	}

	private static void commit(List<NameGroup> groups) {
		foreach (NameGroup g in groups)
			foreach (Target t in g.Targets)
				t.Name = g.Resolved + t.Suffix;
	}

	private static IEnumerable<string> namesOf(NameGroup g) {
		if (g.Targets.Count == 0) {
			yield return g.Resolved;
			yield break;
		}
		foreach (Target t in g.Targets)
			yield return g.Resolved + t.Suffix;
	}

	private static string candidate(NameGroup g, int tier) {
		if (g.Method is null)
			return tier < maxTier ? g.BaseName : g.BaseName + "_x" + hash(g.CanonicalKey);

		return tier switch {
			1 => g.BaseName,
			2 => mangled(g),
			_ => mangled(g) + "_x" + hash(g.CanonicalKey),
		};
	}

	private static string mangled(NameGroup g) {
		MethodDefinition method = g.Method!;
		MethodSignature sig = method.Signature!;
		StringBuilder sb = new(g.BaseName);

		if (method.GenericParameters.Count > 0)
			sb.Append("_g").Append(method.GenericParameters.Count);

		if (sig.ParameterTypes.Count == 0) {
			if (method.GenericParameters.Count == 0)
				sb.Append("__none");
			return sb.ToString();
		}

		sb.Append("__");
		for (int i = 0; i < sig.ParameterTypes.Count; i++) {
			if (i > 0)
				sb.Append('_');
			ParameterDefinition? def = findParamDefinition(method, i);
			sb.Append(TypeNameRenderer.RenderParameter(sig.ParameterTypes[i], def));
		}
		return sb.ToString();
	}

	private static ParameterDefinition? findParamDefinition(MethodDefinition method, int index) {
		foreach (ParameterDefinition def in method.ParameterDefinitions)
			if (def.Sequence == index + 1)
				return def;
		return null;
	}

	private static string hash(string canonical) {
		Span<byte> buf = stackalloc byte[32];
		SHA256.HashData(Encoding.UTF8.GetBytes(canonical), buf);
		return Convert.ToHexStringLower(buf[..8]);
	}

	public static string MirrorTypeName(TypeDefinition type) {
		string name = TypeNameRenderer.Sanitize(
			TypeNameRenderer.StripArity(type.Name?.Value ?? "unknown")
		);
		int arity = type.GenericParameters.Count - (type.DeclaringType?.GenericParameters.Count ?? 0);
		return arity > 0 ? $"{name}_g{arity}" : name;
	}
}
