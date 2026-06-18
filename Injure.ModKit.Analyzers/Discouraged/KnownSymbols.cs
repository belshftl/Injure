// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Discouraged;

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

	public INamedTypeSymbol? IModLoadContext { get; }
	public INamedTypeSymbol? IModLinkContext { get; }
	public INamedTypeSymbol? IModActivateContext { get; }
	public INamedTypeSymbol? IModReloadContext { get; }

	public KnownSymbols(Compilation comp) {
		Hook = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Hook);
		ILHook = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ILHook);
		NativeHook = comp.GetTypeByMetadataName(KnownTypeMetadataNames.NativeHook);
		DetourConfig = comp.GetTypeByMetadataName(KnownTypeMetadataNames.DetourConfig);
		ILCursor = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ILCursor);
		ILContext = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ILContext);
		Instruction = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Instruction);

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

		IModLoadContext = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IModLoadContext);
		IModLinkContext = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IModLinkContext);
		IModActivateContext = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IModActivateContext);
		IModReloadContext = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IModReloadContext);
	}
}
