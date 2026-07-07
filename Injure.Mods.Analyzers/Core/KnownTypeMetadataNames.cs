// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Analyzers.Core;

internal static class KnownTypeMetadataNames {
	public const string ModAssemblyAttribute = "Injure.Mods.Abstractions.ModAssemblyAttribute";
	public const string ModAssemblyHotReloadLevel = "Injure.Mods.Abstractions.ModAssemblyHotReloadLevel";

	public const string ModLifetimeIdentityInterface = "Injure.Mods.Abstractions.IModLifetimeIdentity";
	public const string ModLifetimeIdentityBelongsToAttribute = "Injure.Mods.Abstractions.ModLifetimeIdentityBelongsToAttribute";

	public const string ModEntrypointAttribute = "Injure.Mods.Abstractions.ModEntrypointAttribute";
	public const string ModReloadEntrypointAttribute = "Injure.Mods.Abstractions.ModReloadEntrypointAttribute";
	public const string ModEntrypointInterface = "Injure.Mods.Abstractions.IModEntrypoint`2";
	public const string ModReloadEntrypointInterface = "Injure.Mods.Abstractions.IModReloadEntrypoint`2";

	public const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";
}
