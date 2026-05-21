// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Injure.ModKit.Abstractions;

public readonly record struct LoadedOwnerInfo(
	string OwnerID,
	Semver Version,
	ReloadGeneration Generation
);

public interface IModLoadContext<out TGameApi, TLifetime> where TLifetime : struct, IModLifetimeIdentity {
	string OwnerID { get; }
	Semver Version { get; }
	TGameApi Api { get; }
	IOwnerDiagnostics Diagnostics { get; }
	IActiveOwnerScope<TLifetime> Scope { get; }
	ReloadGeneration Generation { get; }
}

public interface IModLinkContext<out TGameApi, TLifetime> : IModLoadContext<TGameApi, TLifetime> where TLifetime : struct, IModLifetimeIdentity {
	bool TryGetLoadedDependency(string ownerID, out LoadedOwnerInfo info);
	// TODO: something to get the `Assembly` of a code mod dep
}

public interface IModReloadContext<out TGameApi, TLifetime> where TLifetime : struct, IModLifetimeIdentity {
	TGameApi Api { get; }
	ReloadGeneration Generation { get; }
	IReadOnlySet<string> ReloadSet { get; }
	IOwnerDiagnostics Diagnostics { get; }
	IActiveOwnerScope<TLifetime> Scope { get; }
}
