// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Analyzers.Core;

internal static class KnownTypeMetadataNames {
	public const string ModAssemblyAttribute = "Injure.Mods.ModAssemblyAttribute";
	public const string ModAssemblyHotReloadLevel = "Injure.Mods.ModAssemblyHotReloadLevel";

	public const string ModLifetimeIdentityInterface = "Injure.Mods.IModLifetimeIdentity";
	public const string ModLifetimeIdentityBelongsToAttribute = "Injure.Mods.ModLifetimeIdentityBelongsToAttribute";

	public const string ModEntrypointAttribute = "Injure.Mods.ModEntrypointAttribute";
	public const string ModReloadEntrypointAttribute = "Injure.Mods.ModReloadEntrypointAttribute";
	public const string ModEntrypointInterface = "Injure.Mods.IModEntrypoint`2";
	public const string ModReloadEntrypointInterface = "Injure.Mods.IModReloadEntrypoint`2";

	public const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";
}
