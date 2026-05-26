// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Reflection;

using Injure.Core;

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

public interface IModLoadContext<out TGameApi, L> where L : struct, IModLifetimeIdentity {
	string OwnerID { get; }
	Semver Version { get; }
	TGameApi Api { get; }
	IOwnerDiagnostics Diagnostics { get; }
	IActiveOwnerScope<L> Scope { get; }
	ReloadGeneration Generation { get; }
}

public interface IModLinkContext<out TGameApi, L> : IModLoadContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IReadOnlyCollection<LoadedDependencyInfo> LoadedDependencies { get; }
	bool TryGetDependency(string ownerID, out LoadedDependencyInfo info);
	bool TryGetCodeDependency(string ownerID, out LoadedCodeDependencyInfo info);
	LoadedDependencyInfo RequireDependency(string ownerID);
	LoadedCodeDependencyInfo RequireCodeDependency(string ownerID);
}

public interface IModActivateContext<out TGameApi, L> : IModLoadContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	GameServices GameServices { get; }
}

public interface IModReloadContext<out TGameApi, L> : IModLoadContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IReadOnlySet<string> ReloadSet { get; }
	GameServices? GameServices { get; }
}
