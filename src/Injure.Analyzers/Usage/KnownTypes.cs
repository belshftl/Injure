// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Usage;

internal sealed class KnownTypes(Compilation comp) {
	public INamedTypeSymbol? Attribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Attribute);
	public INamedTypeSymbol? IReadOnlyCollection { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IReadOnlyCollection);
	public INamedTypeSymbol? ICollection { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ICollection);
	public INamedTypeSymbol? IReadOnlyDictionary { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IReadOnlyDictionary);

	public INamedTypeSymbol? DontCacheAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.DontCacheAttribute);
	public INamedTypeSymbol? DontCaptureIntoClosureAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.DontCaptureIntoClosureAttribute);
	public INamedTypeSymbol? DontImplementAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.DontImplementAttribute);
	public INamedTypeSymbol? MethodAttributeUsageAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.MethodAttributeUsageAttribute);
}
