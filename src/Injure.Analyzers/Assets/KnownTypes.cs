// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Assets;

internal sealed class KnownTypes(Compilation comp) {
	public INamedTypeSymbol? AssetBorrow { get; } = comp.GetTypeByMetadataName(KnownNames.AssetBorrow);
}
