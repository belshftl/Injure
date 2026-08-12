// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Mods.Analyzers.BannedApis;

internal static class KnownMetadataNames {
	public static readonly ImmutableHashSet<string> MonoModAssemblies = ImmutableHashSet.Create(
		"MonoMod",
		"MonoMod.RuntimeDetour",
		"MonoMod.Utils"
	).WithComparer(StringComparer.Ordinal);
	public static readonly ImmutableHashSet<string> HarmonyAssemblies = ImmutableHashSet.Create(
		"0Harmony",
		"Harmony"
	).WithComparer(StringComparer.Ordinal);

	public const string MonoModRootNamespace = "MonoMod";
}
