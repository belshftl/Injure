// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Draw;

internal sealed class KnownTypes(Compilation comp) {
	public INamedTypeSymbol? Canvas { get; } = comp.GetTypeByMetadataName(KnownNames.Canvas);
	public INamedTypeSymbol? CanvasParamsOverride { get; } = comp.GetTypeByMetadataName(KnownNames.CanvasParamsOverride);
}
