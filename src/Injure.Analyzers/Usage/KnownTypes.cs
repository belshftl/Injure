// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Usage;

internal sealed class KnownTypes(Compilation comp) {
	public INamedTypeSymbol? Attribute { get; } = comp.GetTypeByMetadataName(KnownNames.Attribute);
	public INamedTypeSymbol? IReadOnlyCollection { get; } = comp.GetTypeByMetadataName(KnownNames.IReadOnlyCollection);
	public INamedTypeSymbol? ICollection { get; } = comp.GetTypeByMetadataName(KnownNames.ICollection);
	public INamedTypeSymbol? IReadOnlyDictionary { get; } = comp.GetTypeByMetadataName(KnownNames.IReadOnlyDictionary);

	public INamedTypeSymbol? DontCacheAttribute { get; } = comp.GetTypeByMetadataName(KnownNames.DontCacheAttribute);
	public INamedTypeSymbol? DontCaptureIntoClosureAttribute { get; } = comp.GetTypeByMetadataName(KnownNames.DontCaptureIntoClosureAttribute);
	public INamedTypeSymbol? DontImplementAttribute { get; } = comp.GetTypeByMetadataName(KnownNames.DontImplementAttribute);
	public INamedTypeSymbol? MethodAttributeUsageAttribute { get; } = comp.GetTypeByMetadataName(KnownNames.MethodAttributeUsageAttribute);
}
