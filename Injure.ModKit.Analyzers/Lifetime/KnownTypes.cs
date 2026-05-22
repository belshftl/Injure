// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class KnownTypes(Compilation compilation) {
	public INamedTypeSymbol? Hook { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.Hook);
	public INamedTypeSymbol? ILHook { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.ILHook);
	public INamedTypeSymbol? NativeDetour { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.NativeDetour);

	public INamedTypeSymbol? IDisposable { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.IDisposable);
	public INamedTypeSymbol? IAsyncDisposable { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.IAsyncDisposable);

	public INamedTypeSymbol? Thread { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.Thread);
	public INamedTypeSymbol? Timer { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.Timer);
	public INamedTypeSymbol? TimersTimer { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.TimersTimer);
	public INamedTypeSymbol? PeriodicTimer { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.PeriodicTimer);
	public INamedTypeSymbol? CancellationTokenSource { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.CancellationTokenSource);
	public INamedTypeSymbol? CancellationToken { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.CancellationToken);

	public INamedTypeSymbol? Task { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.Task);
	public INamedTypeSymbol? ValueTask { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.ValueTask);
	public INamedTypeSymbol? ThreadPool { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.ThreadPool);

	public INamedTypeSymbol? AssemblyLoadContext { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.AssemblyLoadContext);
	public INamedTypeSymbol? Process { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.Process);

	public INamedTypeSymbol? GenerationCancellationToken { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.GenerationCancellationToken);
	public INamedTypeSymbol? IActiveOwnerScope { get; } = compilation.GetTypeByMetadataName(KnownTypeMetadataNames.IActiveOwnerScope);

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
		foreach (INamedTypeSymbol implemented in type.AllInterfaces)
			if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, iface.OriginalDefinition))
				return true;
		return false;
	}
}
