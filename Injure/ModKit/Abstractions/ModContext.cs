// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Reflection;

using Injure.Core;
using Injure.ModKit.Abstractions.MonoMod;

namespace Injure.ModKit.Abstractions;

public readonly record struct LoadedDependencyInfo(
	string OwnerID,
	Semver Version,
	ReloadGeneration Generation
);

public readonly record struct LoadedCodeDependencyInfo(
	string OwnerID,
	Semver Version,
	ReloadGeneration Generation,
	Assembly Assembly
);

public interface IModContext<out TGameApi, L> where L : struct, IModLifetimeIdentity {
	string OwnerID { get; }
	Semver Version { get; }
	TGameApi Api { get; }
	IOwnerDiagnostics Diagnostics { get; }
	IBoundedScope<L> Scope { get; }
	ReloadGeneration Generation { get; }
	DiagnosticsSinkRegistry DiagnosticsSinkRegistry { get; }
}

public interface IModLoadContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IModHookDeclarations<L> LoadHooks { get; }
}

public interface IModLinkContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IReadOnlyCollection<LoadedDependencyInfo> LoadedDependencies { get; }
	bool TryGetDependency(string ownerID, out LoadedDependencyInfo info);
	bool TryGetCodeDependency(string ownerID, out LoadedCodeDependencyInfo info);
	LoadedDependencyInfo RequireDependency(string ownerID);
	LoadedCodeDependencyInfo RequireCodeDependency(string ownerID);
	// TODO: IModHookDeclarations<L> LinkHooks { get; }
}

public interface IModActivateContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	GameServices GameServices { get; }
	IBoundedScope<L> ActivationScope { get; }
}

public interface IModReloadContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IReadOnlySet<string> ReloadSet { get; }
	GameServices? GameServices { get; }
}

public sealed class ModLifecycleContextExpiredException : Exception {
	public string Kind { get; }
	public ReloadGeneration Generation { get; }
	internal ModLifecycleContextExpiredException(string kind, ReloadGeneration generation) : base(
		$"{kind} object for {generation} is no longer valid past the method return; cache the values you'd like to keep around such as Api/Scope/Diagnostics, don't retain or capture the whole object"
	) {
		Kind = kind;
		Generation = generation;
	}
}
