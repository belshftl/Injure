// SPDX-License-Identifier: MIT

using System.Linq;
using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class KnownTypes(Compilation comp) {
	public INamedTypeSymbol? Hook { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Hook);
	public INamedTypeSymbol? ILHook { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ILHook);
	public INamedTypeSymbol? NativeDetour { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.NativeDetour);

	public INamedTypeSymbol? IDisposable { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IDisposable);
	public INamedTypeSymbol? IAsyncDisposable { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IAsyncDisposable);

	public INamedTypeSymbol? Thread { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Thread);
	public INamedTypeSymbol? Timer { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Timer);
	public INamedTypeSymbol? TimersTimer { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.TimersTimer);
	public INamedTypeSymbol? PeriodicTimer { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.PeriodicTimer);
	public INamedTypeSymbol? CancellationTokenSource { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.CancellationTokenSource);
	public INamedTypeSymbol? CancellationToken { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.CancellationToken);

	public INamedTypeSymbol? Task { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Task);
	public INamedTypeSymbol? ValueTask { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ValueTask);
	public INamedTypeSymbol? TaskOfT { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.TaskOfT);
	public INamedTypeSymbol? ValueTaskOfT { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ValueTaskOfT);
	public INamedTypeSymbol? ThreadPool { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.ThreadPool);

	public INamedTypeSymbol? AssemblyLoadContext { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.AssemblyLoadContext);
	public INamedTypeSymbol? Process { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.Process);

	public INamedTypeSymbol? AssetStore { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.AssetStore);
	public INamedTypeSymbol? AssetStoreRegistration { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.AssetStoreRegistration);

	public INamedTypeSymbol? GenerationCancellationToken { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.GenerationCancellationToken);
	public INamedTypeSymbol? IActiveOwnerScope { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IActiveOwnerScope);

	public bool IsOrDerivesFrom(ITypeSymbol? type, INamedTypeSymbol? candidateBase) {
		if (type is null || candidateBase is null)
			return false;
		for (ITypeSymbol? sym = type; sym is not null; sym = sym.BaseType)
			if (SymbolEqualityComparer.Default.Equals(sym.OriginalDefinition, candidateBase.OriginalDefinition))
				return true;
		return false;
	}

	public bool Implements(ITypeSymbol? type, INamedTypeSymbol? iface) {
		if (type is null || iface is null)
			return false;
		return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iface.OriginalDefinition));
	}
}
