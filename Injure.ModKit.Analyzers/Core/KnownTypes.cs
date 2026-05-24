// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Core;

internal sealed class KnownTypes(Compilation comp) {
	public INamedTypeSymbol? ModAssemblyAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModAssemblyAttribute);
	public INamedTypeSymbol? ModAssemblyHotReloadLevel { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModAssemblyHotReloadLevel);
	public INamedTypeSymbol? ModLifetimeIdentityMarkerAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModLifetimeIdentityMarkerAttribute);
	public INamedTypeSymbol? ModLifetimeIdentityInterface { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModLifetimeIdentityInterface);
	public INamedTypeSymbol? ModEntrypointAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModEntrypointAttribute);
	public INamedTypeSymbol? ModReloadEntrypointAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModReloadEntrypointAttribute);
	public INamedTypeSymbol? ModEntrypointInterface { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModEntrypointInterface);
	public INamedTypeSymbol? ModReloadEntrypointInterface { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModReloadEntrypointInterface);
	public INamedTypeSymbol? AnalyzerStampAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.AnalyzerStampAttribute);
}

internal static class KnownTypeMetadataNames {
	public const string ModAssemblyAttribute = "Injure.ModKit.Abstractions.ModAssemblyAttribute";
	public const string ModAssemblyHotReloadLevel = "Injure.ModKit.Abstractions.ModAssemblyHotReloadLevel";

	public const string ModLifetimeIdentityMarkerAttribute = "Injure.ModKit.Abstractions.ModLifetimeIdentityMarkerAttribute";
	public const string ModLifetimeIdentityInterface = "Injure.ModKit.Abstractions.IModLifetimeIdentity";

	public const string ModEntrypointAttribute = "Injure.ModKit.Abstractions.ModEntrypointAttribute";
	public const string ModReloadEntrypointAttribute = "Injure.ModKit.Abstractions.ModReloadEntrypointAttribute";
	public const string ModEntrypointInterface = "Injure.ModKit.Abstractions.IModEntrypoint`2";
	public const string ModReloadEntrypointInterface = "Injure.ModKit.Abstractions.IModReloadEntrypoint`2";

	public const string AnalyzerStampAttribute = "Injure.ModKit.Abstractions.ModAnalyzerStampAttribute";
	public const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";
}
