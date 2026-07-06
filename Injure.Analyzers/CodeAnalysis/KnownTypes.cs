// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.CodeAnalysis;

internal sealed class KnownTypes(Compilation comp) {
	public INamedTypeSymbol? Attribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Attribute);
	public INamedTypeSymbol? DontImplementAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.DontImplementAttribute);
	public INamedTypeSymbol? MethodAttributeUsageAttribute { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.MethodAttributeUsageAttribute);
}
