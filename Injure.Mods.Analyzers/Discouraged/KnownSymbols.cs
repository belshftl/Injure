// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Discouraged;

internal sealed class KnownSymbols {
	public INamedTypeSymbol? Hook { get; }
	public INamedTypeSymbol? ILHook { get; }
	public INamedTypeSymbol? NativeHook { get; }
	public INamedTypeSymbol? DetourConfig { get; }
	public INamedTypeSymbol? ILCursor { get; }
	public INamedTypeSymbol? ILContext { get; }
	public INamedTypeSymbol? Instruction { get; }
	public ImmutableHashSet<IMethodSymbol> EmitDelegateMethods { get; }
	public ImmutableHashSet<IMethodSymbol> RemoveInstructionMethods { get; }
	public ImmutableHashSet<IMethodSymbol> GotoMethods { get; }
	public ImmutableHashSet<ISymbol> InstructionMembers { get; }

	public INamespaceSymbol? ModInterop { get; }
	public INamedTypeSymbol? ModInteropManager { get; }
	public INamedTypeSymbol? ModExportNameAttribute { get; }
	public INamedTypeSymbol? ModImportNameAttribute { get; }

	public INamedTypeSymbol? IModLoadContext { get; }
	public INamedTypeSymbol? IModLinkContext { get; }
	public INamedTypeSymbol? IModActivateContext { get; }
	public INamedTypeSymbol? IModReloadContext { get; }

	public KnownSymbols(Compilation comp) {
		Hook = comp.GetTypeByMetadataName(KnownMetadataNames.Hook);
		ILHook = comp.GetTypeByMetadataName(KnownMetadataNames.ILHook);
		NativeHook = comp.GetTypeByMetadataName(KnownMetadataNames.NativeHook);
		DetourConfig = comp.GetTypeByMetadataName(KnownMetadataNames.DetourConfig);
		ILCursor = comp.GetTypeByMetadataName(KnownMetadataNames.ILCursor);
		ILContext = comp.GetTypeByMetadataName(KnownMetadataNames.ILContext);
		Instruction = comp.GetTypeByMetadataName(KnownMetadataNames.Instruction);

		ModInterop = getNamespace(comp, KnownMetadataNames.ModInterop);
		ModInteropManager = comp.GetTypeByMetadataName(KnownMetadataNames.ModInteropManager);
		ModExportNameAttribute = comp.GetTypeByMetadataName(KnownMetadataNames.ModExportNameAttribute);
		ModImportNameAttribute = comp.GetTypeByMetadataName(KnownMetadataNames.ModImportNameAttribute);

		EmitDelegateMethods = ILCursor
			?.GetMembers()
			.OfType<IMethodSymbol>()
			.Where(static m => m.Name == "EmitDelegate")
			.Select(static m => m.OriginalDefinition)
			.ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default) ?? ImmutableHashSet<IMethodSymbol>.Empty;
		RemoveInstructionMethods = ILCursor
			?.GetMembers()
			.OfType<IMethodSymbol>()
			.Where(static m => m.Name is "Remove" or "RemoveRange")
			.Select(static m => m.OriginalDefinition)
			.ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default) ?? ImmutableHashSet<IMethodSymbol>.Empty;
		GotoMethods = ILCursor
			?.GetMembers()
			.OfType<IMethodSymbol>()
			.Where(static m => m.Name is "GotoNext" or "GotoPrev")
			.Select(static m => m.OriginalDefinition)
			.ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default) ?? ImmutableHashSet<IMethodSymbol>.Empty;
		InstructionMembers = Instruction
			?.GetMembers()
			.Where(static m => m.Name is "OpCode" or "Operand")
			.Select(static m => m.OriginalDefinition)
			.ToImmutableHashSet(SymbolEqualityComparer.Default) ?? ImmutableHashSet<ISymbol>.Empty;

		IModLoadContext = comp.GetTypeByMetadataName(KnownMetadataNames.IModLoadContext);
		IModLinkContext = comp.GetTypeByMetadataName(KnownMetadataNames.IModLinkContext);
		IModActivateContext = comp.GetTypeByMetadataName(KnownMetadataNames.IModActivateContext);
		IModReloadContext = comp.GetTypeByMetadataName(KnownMetadataNames.IModReloadContext);
	}

	private static INamespaceSymbol? getNamespace(Compilation comp, string qualifiedName) {
		INamespaceSymbol curr = comp.GlobalNamespace;
		foreach (string component in qualifiedName.Split('.')) {
			curr = curr.GetMembers(component).OfType<INamespaceSymbol>().SingleOrDefault();
			if (curr is null)
				return null;
		}
		return curr;
	}
}
