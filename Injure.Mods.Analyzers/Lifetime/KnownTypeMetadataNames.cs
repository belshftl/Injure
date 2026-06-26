// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Analyzers.Lifetime;

internal static class KnownTypeMetadataNames {
	public const string Hook = "MonoMod.RuntimeDetour.Hook";
	public const string ILHook = "MonoMod.RuntimeDetour.ILHook";
	public const string NativeHook = "MonoMod.RuntimeDetour.NativeHook";

	public const string IDisposable = "System.IDisposable";
	public const string IAsyncDisposable = "System.IAsyncDisposable";
	public const string Exception = "System.Exception";

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

	public const string BoundedCt = "Injure.Mods.BoundedCt`1";

	public const string DoesNotCreateObligationAttribute = "Injure.Mods.CodeAnalysis.DoesNotCreateObligationAttribute";
	public const string ObligationAttribute = "Injure.Mods.CodeAnalysis.ObligationAttribute";
	public const string SatisfiesAndReturnsObligationAttribute = "Injure.Mods.CodeAnalysis.SatisfiesAndReturnsObligationAttribute";
	public const string SatisfiesObjectObligationAttribute = "Injure.Mods.CodeAnalysis.SatisfiesObjectObligationAttribute";
	public const string SatisfiesObligationAttribute = "Injure.Mods.CodeAnalysis.SatisfiesObligationAttribute";
}
