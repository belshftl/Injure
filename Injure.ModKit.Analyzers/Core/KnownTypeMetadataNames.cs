// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.ModKit.Analyzers.Core;

internal static class KnownTypeMetadataNames {
	public const string ModAssemblyAttribute = "Injure.ModKit.Abstractions.ModAssemblyAttribute";
	public const string ModAssemblyHotReloadLevel = "Injure.ModKit.Abstractions.ModAssemblyHotReloadLevel";

	public const string ModLifetimeIdentityInterface = "Injure.ModKit.Abstractions.IModLifetimeIdentity";
	public const string ModLifetimeIdentityBelongsToAttribute = "Injure.ModKit.Abstractions.ModLifetimeIdentityBelongsToAttribute";

	public const string ModEntrypointAttribute = "Injure.ModKit.Abstractions.ModEntrypointAttribute";
	public const string ModReloadEntrypointAttribute = "Injure.ModKit.Abstractions.ModReloadEntrypointAttribute";
	public const string ModEntrypointInterface = "Injure.ModKit.Abstractions.IModEntrypoint`2";
	public const string ModReloadEntrypointInterface = "Injure.ModKit.Abstractions.IModReloadEntrypoint`2";

	public const string AnalyzerStampAttribute = "Injure.ModKit.Abstractions.ModAnalyzerStampAttribute";
	public const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";
}
