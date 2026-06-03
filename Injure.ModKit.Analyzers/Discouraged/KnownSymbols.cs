// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Discouraged;

internal sealed class KnownSymbols(Compilation comp) {
	public INamedTypeSymbol? Hook { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Hook);
	public INamedTypeSymbol? ILHook { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ILHook);
	public INamedTypeSymbol? NativeHook { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.NativeHook);
	public INamedTypeSymbol? DetourConfig { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.DetourConfig);
	public FrozenSet<IMethodSymbol> EmitDelegateMethods { get; } =
		comp.GetTypeByMetadataName(KnownTypeMetadataNames.ILCursor)
			?.GetMembers()
			.OfType<IMethodSymbol>()
			.Where(static m => m.Name == "EmitDelegate")
			.Select(static m => m.OriginalDefinition)
			.ToFrozenSet<IMethodSymbol>(SymbolEqualityComparer.Default) ?? FrozenSet<IMethodSymbol>.Empty;

	public INamedTypeSymbol? IModLoadContext { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IModLoadContext);
	public INamedTypeSymbol? IModLinkContext { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IModLinkContext);
	public INamedTypeSymbol? IModActivateContext { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IModActivateContext);
	public INamedTypeSymbol? IModReloadContext { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IModReloadContext);
}
