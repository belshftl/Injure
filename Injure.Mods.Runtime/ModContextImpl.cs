// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

using Injure.Mods.MonoMod;
using Injure.Mods.Runtime.MonoMod;
using Injure.Runtime;

namespace Injure.Mods.Runtime;

internal abstract class ModContextImpl<TGameApi, L>(
	string typeName,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
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
	public DiagnosticsSinkRegistry DiagnosticsSinkRegistry {
		get => field ?? throw new ModLifecycleContextExpiredException(typeName, Generation);
		private set;
	} = diagnosticsSinkRegistry;

	public abstract void OnDropStrongReferences();

	public void DropStrongReferences() {
		OnDropStrongReferences();
		gameApiDropped = true;
		Api = default!;
		Diagnostics = null!;
		Scope = null!;
		DiagnosticsSinkRegistry = null!;
	}
}

internal sealed class ModLoadContextImpl<TGameApi, L>(
	ModHookDeclarations<TGameApi, L> loadHooks,
	UntypedModExportTable exports,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : ModContextImpl<TGameApi, L>(nameof(IModLoadContext<,>), ownerID, version, api, diagnostics, scope, diagnosticsSinkRegistry), IModLoadContext<TGameApi, L>
	where L : struct, IModLifetimeIdentity {
	private ModHookDeclarations<TGameApi, L>? loadHooks = loadHooks;
	public IModHookDeclarations<L> LoadHooks => loadHooks ?? throw new ModLifecycleContextExpiredException(nameof(IModLoadContext<,>), Generation);
	public IModExportDeclarations<L> Exports {
		get => field ?? throw new ModLifecycleContextExpiredException(nameof(IModLoadContext<,>), Generation);
		private set;
	} = exports.AsDeclsView<L>();

	public override void OnDropStrongReferences() {
		loadHooks?.DropStrongReferences();
		loadHooks = null;
		Exports = null!;
	}
}

internal sealed class ModLinkContextImpl<TGameApi, L>(
	IReadOnlyDictionary<string, UntypedLoadedDepInfo> loaded,
	IReadOnlyDictionary<string, UntypedUntypedLoadedCodeDepInfo> loadedCode,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : ModContextImpl<TGameApi, L>(nameof(IModLinkContext<,>), ownerID, version, api, diagnostics, scope, diagnosticsSinkRegistry), IModLinkContext<TGameApi, L>
	where L : struct, IModLifetimeIdentity {
	private IReadOnlyDictionary<string, UntypedLoadedDepInfo>? loaded = loaded;
	private IReadOnlyDictionary<string, UntypedUntypedLoadedCodeDepInfo>? loadedCode = loadedCode;

	public bool TryGetDependency(string ownerID, out LoadedDepInfo<L> info) {
		if (loaded is null)
			throw new ModLifecycleContextExpiredException(nameof(IModLinkContext<,>), Generation);
		if (loaded.TryGetValue(ownerID, out UntypedLoadedDepInfo u)) {
			info = new LoadedDepInfo<L>(u.OwnerID, u.Version, u.Generation, u.Scope);
			return true;
		}
		info = default;
		return false;
	}

	public bool TryGetCodeDependency(string ownerID, out UntypedLoadedCodeDepInfo<L> info) {
		if (loadedCode is null)
			throw new ModLifecycleContextExpiredException(nameof(IModLinkContext<,>), Generation);
		if (loadedCode.TryGetValue(ownerID, out UntypedUntypedLoadedCodeDepInfo u)) {
			info = new UntypedLoadedCodeDepInfo<L>(u.LifetimeIdentityType, u.OwnerID, u.Version, u.Generation, u.Scope, u.Assembly);
			return true;
		}
		info = default;
		return false;
	}

	public bool TryGetCodeDependency<LDependency>(out LoadedCodeDepInfo<L, LDependency> info) where LDependency : struct, IModLifetimeIdentity {
		if (loadedCode is null)
			throw new ModLifecycleContextExpiredException(nameof(IModLinkContext<,>), Generation);
		string ownerID = ModLifetimeOwnerInference.Infer<LDependency>();
		if (loadedCode.TryGetValue(ownerID, out UntypedUntypedLoadedCodeDepInfo u)) {
			info = new LoadedCodeDepInfo<L, LDependency>(
				u.OwnerID,
				u.Version,
				u.Generation,
				u.Scope.AsTyped<LDependency>(),
				u.Exports.AsTableView<L, LDependency>(Generation),
				u.Assembly
			);
			return true;
		}
		info = default;
		return false;
	}

	public LoadedDepInfo<L> RequireDependency(string ownerID) => TryGetDependency(ownerID, out LoadedDepInfo<L> info)
		? info
		: throw new ModLoadException(OwnerID, $"declared dependency '{ownerID}' is not loaded");
	public UntypedLoadedCodeDepInfo<L> RequireCodeDependency(string ownerID) => TryGetCodeDependency(ownerID, out UntypedLoadedCodeDepInfo<L> info)
		? info
		: throw new ModLoadException(OwnerID, $"declared code dependency '{ownerID}' is not loaded");
	public LoadedCodeDepInfo<L, LDependency> RequireCodeDependency<LDependency>() where LDependency : struct, IModLifetimeIdentity =>
		TryGetCodeDependency(out LoadedCodeDepInfo<L, LDependency> info)
			? info
			: throw new ModLoadException(OwnerID, $"declared code dependency wiht lifetime identity type '{typeof(LDependency)}' is not loaded");

	public override void OnDropStrongReferences() {
		loaded = null;
		loadedCode = null;
	}
}

internal sealed class ModActivateContextImpl<TGameApi, L>(
	GameServices gameServices,
	UntypedBoundedScopeImpl activationScope,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : ModContextImpl<TGameApi, L>(nameof(IModActivateContext<,>), ownerID, version, api, diagnostics, scope, diagnosticsSinkRegistry), IModActivateContext<TGameApi, L>
	where L : struct, IModLifetimeIdentity {
	public GameServices GameServices {
		get => field ?? throw new ModLifecycleContextExpiredException(nameof(IModActivateContext<,>), Generation);
		private set;
	} = gameServices;
	public IBoundedScope<L> ActivationScope {
		get => field ?? throw new ModLifecycleContextExpiredException(nameof(IModActivateContext<,>), Generation);
		private set;
	} = activationScope.AsTyped<L>();

	public override void OnDropStrongReferences() {
		GameServices = null!;
		ActivationScope = null!;
	}
}

internal sealed class ModReloadContextImpl<TGameApi, L>(
	GameServices? gameServices,
	IReadOnlySet<string> reloadSet,
	string ownerID,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : ModContextImpl<TGameApi, L>(nameof(IModReloadContext<,>), ownerID, version, api, diagnostics, scope, diagnosticsSinkRegistry), IModReloadContext<TGameApi, L>
	where L : struct, IModLifetimeIdentity {
	private bool gameServicesDropped = false;
	private GameServices? gameServices = gameServices;

	public IReadOnlySet<string> ReloadSet { get; } = reloadSet;
	public GameServices? GameServices => !gameServicesDropped ? gameServices : throw new ModLifecycleContextExpiredException(nameof(IModReloadContext<,>), Generation);

	public override void OnDropStrongReferences() {
		gameServicesDropped = true;
		gameServices = null;
	}
}
