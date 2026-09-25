// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Modif;
using Injure.Mods.Runtime.Modif;
using Injure.Runtime;

namespace Injure.Mods.Runtime;

internal abstract class ModCtxImpl<TGameApi, L>(
	string typeName,
	string ownerId,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : IStrongRefDroppable, IModCtx<TGameApi, L> where L : struct, IModLifetimeIdentity {
	protected readonly ReloadGeneration Generation = scope.Generation;
	private bool gameApiDropped = false;

	public string OwnerId { get; } = ownerId;
	public Semver Version { get; } = version;
	public TGameApi Api {
		get => !gameApiDropped ? field : throw new ModLifecycleCtxExpiredException(typeName, Generation);
		private set;
	} = api;
	public IOwnerDiagnostics Diagnostics {
		get => field ?? throw new ModLifecycleCtxExpiredException(typeName, Generation);
		private set;
	} = diagnostics;
	public IBoundedScope<L> Scope {
		get => field ?? throw new ModLifecycleCtxExpiredException(typeName, Generation);
		private set;
	} = scope.AsTyped<L>();
	ReloadGeneration IModCtx<TGameApi, L>.Generation => Generation;
	public DiagnosticsSinkRegistry DiagnosticsSinkRegistry {
		get => field ?? throw new ModLifecycleCtxExpiredException(typeName, Generation);
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

internal sealed class ModLoadCtxImpl<TGameApi, L>(
	DetourDeclImpl<L> loadDetours,
	PatchDeclImpl<L> loadPatches,
	UntypedModExportTable exports,
	string ownerId,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : ModCtxImpl<TGameApi, L>(nameof(IModLoadCtx<,>), ownerId, version, api, diagnostics, scope, diagnosticsSinkRegistry), IModLoadCtx<TGameApi, L>
	where L : struct, IModLifetimeIdentity {
	private DetourDeclImpl<L>? loadDetours = loadDetours;
	public IDetourDecl<L> LoadDetours => loadDetours ?? throw new ModLifecycleCtxExpiredException(nameof(IModLoadCtx<,>), Generation);
	private PatchDeclImpl<L>? loadPatches = loadPatches;
	public IPatchDecl<L> LoadPatches => loadPatches ?? throw new ModLifecycleCtxExpiredException(nameof(IModLoadCtx<,>), Generation);
	public IModExportDecl<L> Exports {
		get => field ?? throw new ModLifecycleCtxExpiredException(nameof(IModLoadCtx<,>), Generation);
		private set;
	} = exports.AsDeclsView<L>();

	public override void OnDropStrongReferences() {
		loadDetours = null;
		loadPatches = null;
		Exports = null!;
	}
}

internal sealed class ModLinkCtxImpl<TGameApi, L>(
	IReadOnlyDictionary<string, UntypedLoadedDepInfo> loaded,
	IReadOnlyDictionary<string, UntypedUntypedLoadedCodeDepInfo> loadedCode,
	string ownerId,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : ModCtxImpl<TGameApi, L>(nameof(IModLinkCtx<,>), ownerId, version, api, diagnostics, scope, diagnosticsSinkRegistry), IModLinkCtx<TGameApi, L>
	where L : struct, IModLifetimeIdentity {
	private IReadOnlyDictionary<string, UntypedLoadedDepInfo>? loaded = loaded;
	private IReadOnlyDictionary<string, UntypedUntypedLoadedCodeDepInfo>? loadedCode = loadedCode;

	public bool TryGetDep(string ownerId, out LoadedDepInfo<L> info) {
		if (loaded is null)
			throw new ModLifecycleCtxExpiredException(nameof(IModLinkCtx<,>), Generation);
		if (loaded.TryGetValue(ownerId, out UntypedLoadedDepInfo u)) {
			info = new LoadedDepInfo<L>(u.OwnerId, u.Version, u.Generation, u.Scope);
			return true;
		}
		info = default;
		return false;
	}

	public bool TryGetCodeDep(string ownerId, out UntypedLoadedCodeDepInfo<L> info) {
		if (loadedCode is null)
			throw new ModLifecycleCtxExpiredException(nameof(IModLinkCtx<,>), Generation);
		if (loadedCode.TryGetValue(ownerId, out UntypedUntypedLoadedCodeDepInfo u)) {
			info = new UntypedLoadedCodeDepInfo<L>(u.LifetimeIdentityType, u.OwnerId, u.Version, u.Generation, u.Scope, u.Assembly);
			return true;
		}
		info = default;
		return false;
	}

	public bool TryGetCodeDep<LDep>(out LoadedCodeDepInfo<L, LDep> info) where LDep : struct, IModLifetimeIdentity {
		if (loadedCode is null)
			throw new ModLifecycleCtxExpiredException(nameof(IModLinkCtx<,>), Generation);
		string ownerId = ModLifetimeOwnerInference.Infer<LDep>();
		if (loadedCode.TryGetValue(ownerId, out UntypedUntypedLoadedCodeDepInfo u)) {
			info = new LoadedCodeDepInfo<L, LDep>(
				u.OwnerId,
				u.Version,
				u.Generation,
				u.Scope.AsTyped<LDep>(),
				u.Exports.AsTableView<L, LDep>(Generation),
				u.Assembly
			);
			return true;
		}
		info = default;
		return false;
	}

	public LoadedDepInfo<L> RequireDep(string ownerId) => TryGetDep(ownerId, out LoadedDepInfo<L> info)
		? info
		: throw new ModLoadException(OwnerId, $"declared dependency '{ownerId}' is not loaded");
	public UntypedLoadedCodeDepInfo<L> RequireCodeDep(string ownerId) => TryGetCodeDep(ownerId, out UntypedLoadedCodeDepInfo<L> info)
		? info
		: throw new ModLoadException(OwnerId, $"declared code dependency '{ownerId}' is not loaded");
	public LoadedCodeDepInfo<L, LDep> RequireCodeDep<LDep>() where LDep : struct, IModLifetimeIdentity =>
		TryGetCodeDep(out LoadedCodeDepInfo<L, LDep> info)
			? info
			: throw new ModLoadException(OwnerId, $"declared code dependency with lifetime identity type '{typeof(LDep)}' is not loaded");

	public override void OnDropStrongReferences() {
		loaded = null;
		loadedCode = null;
	}
}

internal sealed class ModActivateCtxImpl<TGameApi, L>(
	GameServices gameServices,
	UntypedBoundedScopeImpl activationScope,
	string ownerId,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : ModCtxImpl<TGameApi, L>(nameof(IModActivateCtx<,>), ownerId, version, api, diagnostics, scope, diagnosticsSinkRegistry), IModActivateCtx<TGameApi, L>
	where L : struct, IModLifetimeIdentity {
	public GameServices GameServices {
		get => field ?? throw new ModLifecycleCtxExpiredException(nameof(IModActivateCtx<,>), Generation);
		private set;
	} = gameServices;
	public IBoundedScope<L> ActivationScope {
		get => field ?? throw new ModLifecycleCtxExpiredException(nameof(IModActivateCtx<,>), Generation);
		private set;
	} = activationScope.AsTyped<L>();

	public override void OnDropStrongReferences() {
		GameServices = null!;
		ActivationScope = null!;
	}
}

internal sealed class ModReloadCtxImpl<TGameApi, L>(
	GameServices? gameServices,
	IReadOnlySet<string> reloadSet,
	string ownerId,
	Semver version,
	TGameApi api,
	IOwnerDiagnostics diagnostics,
	UntypedBoundedScopeImpl scope,
	DiagnosticsSinkRegistry diagnosticsSinkRegistry
) : ModCtxImpl<TGameApi, L>(nameof(IModReloadCtx<,>), ownerId, version, api, diagnostics, scope, diagnosticsSinkRegistry), IModReloadCtx<TGameApi, L>
	where L : struct, IModLifetimeIdentity {
	private bool gameServicesDropped = false;
	private GameServices? gameServices = gameServices;

	public IReadOnlySet<string> ReloadSet { get; } = reloadSet;
	public GameServices? GameServices => !gameServicesDropped ? gameServices : throw new ModLifecycleCtxExpiredException(nameof(IModReloadCtx<,>), Generation);

	public override void OnDropStrongReferences() {
		gameServicesDropped = true;
		gameServices = null;
	}
}
