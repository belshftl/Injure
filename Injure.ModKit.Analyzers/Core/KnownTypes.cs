// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Core;

internal sealed class KnownTypes(Compilation comp) {
	public INamedTypeSymbol? ModAssemblyAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModAssemblyAttribute);
	public INamedTypeSymbol? ModAssemblyHotReloadLevel { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModAssemblyHotReloadLevel);

	public INamedTypeSymbol? ModLifetimeIdentityInterface { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModLifetimeIdentityInterface);
	public INamedTypeSymbol? ModLifetimeIdentityBelongsToAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModLifetimeIdentityBelongsToAttribute);

	public INamedTypeSymbol? ModEntrypointAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModEntrypointAttribute);
	public INamedTypeSymbol? ModReloadEntrypointAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModReloadEntrypointAttribute);
	public INamedTypeSymbol? ModEntrypointInterface { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModEntrypointInterface);
	public INamedTypeSymbol? ModReloadEntrypointInterface { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ModReloadEntrypointInterface);

	public INamedTypeSymbol? AnalyzerStampAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.AnalyzerStampAttribute);
}
