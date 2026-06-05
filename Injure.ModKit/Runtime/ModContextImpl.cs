// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;

using Injure.Core;
using Injure.ModKit.Abstractions;
using Injure.ModKit.Abstractions.MonoMod;
using Injure.ModKit.MonoMod;

namespace Injure.ModKit.Runtime;

internal abstract class ModContextImpl<TGameApi, L>(
	string typeName,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScope scope
) : IStrongRefDroppable, IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	protected readonly ReloadGeneration Generation = scope.Generation;
	private bool gameApiDropped = false;

	public string OwnerID { get; } = ownerID;
	public Semver Version { get; } = version;
	public TGameApi Api {
		get => !gameApiDropped ? field : throw new ModLifecycleContextExpiredException(typeName, Generation);
		private set;
	} = api;
	public IOwnerDiagnostics Diagnostics {
		get => field ?? throw new ModLifecycleContextExpiredException(typeName, Generation);
		private set;
	} = diagnostics;
	public IBoundedScope<L> Scope {
		get => field ?? throw new ModLifecycleContextExpiredException(typeName, Generation);
		private set;
	} = scope.AsTyped<L>();
	ReloadGeneration IModContext<TGameApi, L>.Generation => Generation;

	public abstract void OnDropStrongReferences();

	public void DropStrongReferences() {
		OnDropStrongReferences();
		gameApiDropped = true;
		Api = default!;
		Diagnostics = null!;
		Scope = null!;
	}
}

internal sealed class ModLoadContextImpl<TGameApi, L>(
	ModHookDeclarations<TGameApi, L> loadHooks,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScope scope
) : ModContextImpl<TGameApi, L>(nameof(IModLoadContext<,>), ownerID, version, api, diagnostics, scope), IModLoadContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	private ModHookDeclarations<TGameApi, L>? loadHooks = loadHooks;
	public IModHookDeclarations<L> LoadHooks => loadHooks ?? throw new ModLifecycleContextExpiredException(nameof(IModLoadContext<,>), Generation);

	public override void OnDropStrongReferences() {
		loadHooks?.DropStrongReferences();
		loadHooks = null;
	}
}

internal sealed class ModLinkContextImpl<TGameApi, L>(
	IReadOnlyDictionary<string, LoadedDependencyInfo> loaded,
	IReadOnlyDictionary<string, LoadedCodeDependencyInfo> loadedCode,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScope scope
) : ModContextImpl<TGameApi, L>(nameof(IModLinkContext<,>), ownerID, version, api, diagnostics, scope), IModLinkContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	private IReadOnlyDictionary<string, LoadedDependencyInfo>? loaded = loaded;
	private IReadOnlyDictionary<string, LoadedCodeDependencyInfo>? loadedCode = loadedCode;

	public IReadOnlyCollection<LoadedDependencyInfo> LoadedDependencies { get; } = loaded.Values.ToArray();

	public bool TryGetDependency(string ownerID, out LoadedDependencyInfo info) =>
		loaded is not null ? loaded.TryGetValue(ownerID, out info) : throw new ModLifecycleContextExpiredException(nameof(IModLinkContext<,>), Generation);
	public bool TryGetCodeDependency(string ownerID, out LoadedCodeDependencyInfo info) =>
		loadedCode is not null ? loadedCode.TryGetValue(ownerID, out info) : throw new ModLifecycleContextExpiredException(nameof(IModLinkContext<,>), Generation);
	public LoadedDependencyInfo RequireDependency(string ownerID) => TryGetDependency(ownerID, out LoadedDependencyInfo info)
		? info : throw new ModLoadException(OwnerID, $"declared dependency '{ownerID}' is not loaded");
	public LoadedCodeDependencyInfo RequireCodeDependency(string ownerID) => TryGetCodeDependency(ownerID, out LoadedCodeDependencyInfo info)
		? info : throw new ModLoadException(OwnerID, $"declared code dependency '{ownerID}' is not loaded");

	public override void OnDropStrongReferences() {
		loaded = null;
		loadedCode = null;
	}
}

internal sealed class ModActivateContextImpl<TGameApi, L>(
	GameServices gameServices,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScope scope
) : ModContextImpl<TGameApi, L>(nameof(IModActivateContext<,>), ownerID, version, api, diagnostics, scope), IModActivateContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	public GameServices GameServices {
		get => field ?? throw new ModLifecycleContextExpiredException(nameof(IModActivateContext<,>), Generation);
		private set;
	} = gameServices;

	public override void OnDropStrongReferences() {
		GameServices = null!;
	}
}

internal sealed class ModReloadContextImpl<TGameApi, L>(
	GameServices? gameServices,
	IReadOnlySet<string> reloadSet,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScope scope
) : ModContextImpl<TGameApi, L>(nameof(IModReloadContext<,>), ownerID, version, api, diagnostics, scope), IModReloadContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	private bool gameServicesDropped = false;
	private GameServices? gameServices = gameServices;

	public IReadOnlySet<string> ReloadSet { get; } = reloadSet;
	public GameServices? GameServices => !gameServicesDropped ? gameServices : throw new ModLifecycleContextExpiredException(nameof(IModReloadContext<,>), Generation);

	public override void OnDropStrongReferences() {
		gameServicesDropped = true;
		gameServices = null;
	}
}
