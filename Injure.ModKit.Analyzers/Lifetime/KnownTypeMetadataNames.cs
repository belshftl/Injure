// SPDX-License-Identifier: MIT

namespace Injure.ModKit.Analyzers.Lifetime;

internal static class KnownTypeMetadataNames {
	public const string Hook = "MonoMod.RuntimeDetour.Hook";
	public const string ILHook = "MonoMod.RuntimeDetour.ILHook";
	public const string NativeDetour = "MonoMod.RuntimeDetour.NativeDetour";

	public const string IDisposable = "System.IDisposable";
	public const string IAsyncDisposable = "System.IAsyncDisposable";

	public const string Thread = "System.Threading.Thread";
	public const string Timer = "System.Threading.Timer";
	public const string TimersTimer = "System.Timers.Timer";
	public const string PeriodicTimer = "System.Threading.PeriodicTimer";
	public const string CancellationTokenSource = "System.Threading.CancellationTokenSource";
	public const string CancellationToken = "System.Threading.CancellationToken";

	public const string Task = "System.Threading.Tasks.Task";
	public const string ValueTask = "System.Threading.Tasks.ValueTask";
	public const string TaskOfT = "System.Threading.Tasks.Task`1";
	public const string ValueTaskOfT = "System.Threading.Tasks.ValueTask`1";
	public const string ThreadPool = "System.Threading.ThreadPool";

	public const string AssemblyLoadContext = "System.Runtime.Loader.AssemblyLoadContext";
	public const string Process = "System.Diagnostics.Process";

	public const string AssetStoreRegistration = "Injure.Assets.AssetStoreRegistration";
	public const string TickerHandle = "Injure.Scheduling.TickerHandle";
	public const string TickerSubscriptionHandle = "Injure.Scheduling.TickerSubscriptionHandle";
	public const string IReloadTeardown = "Injure.Assets.IReloadTeardown";

	public const string BoundedCt = "Injure.ModKit.Abstractions.BoundedCt`1";
	public const string IActiveOwnerScope = "Injure.ModKit.Abstractions.IActiveOwnerScope";
	public const string SatisfiesAndReturnsAttribute = "Injure.ModKit.Abstractions.CodeAnalysis.SatisfiesAndReturnsAttribute";
}
