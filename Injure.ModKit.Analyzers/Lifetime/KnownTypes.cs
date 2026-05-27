// SPDX-License-Identifier: MIT

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

	public INamedTypeSymbol? AssetStoreRegistration { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.AssetStoreRegistration);
	public INamedTypeSymbol? TickerHandle { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.TickerHandle);
	public INamedTypeSymbol? TickerSubscriptionHandle { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.TickerSubscriptionHandle);
	public INamedTypeSymbol? IReloadTeardown { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IReloadTeardown);

	public INamedTypeSymbol? BoundedCt { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.BoundedCt);
	public INamedTypeSymbol? IActiveOwnerScope { get; } = comp.GetTypeByMetadataName(KnownTypeMetadataNames.IActiveOwnerScope);
}
