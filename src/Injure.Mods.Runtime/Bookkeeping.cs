// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Runtime.Modif;

namespace Injure.Mods.Runtime;

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct ModSource(string RootDirectory, string ManifestPath) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => RootDirectory is null;
}
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct DiscoveredMod(ModSource Source, ModManifest Manifest) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Manifest is null;
}
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct ResolvedMod(ModManifest Manifest, ModSource Source) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Manifest is null;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct ResolvedModGraph(
	IReadOnlyDictionary<string, ResolvedMod> Mods,
	IReadOnlyDictionary<string, string[]> OutgoingOrderEdges,
	IReadOnlyDictionary<string, string[]> ReloadDependentsByTarget,
	ModWavePlan Waves,
	IReadOnlyList<ResolvedMod> ModsInDeterministicOrder
) : IHasInvalidDefault {
	public static readonly ResolvedModGraph Empty = new(
		new Dictionary<string, ResolvedMod>(),
		new Dictionary<string, string[]>(),
		new Dictionary<string, string[]>(),
		new ModWavePlan([]),
		[]
	);

	bool IHasInvalidDefault.IsDefault => Mods is null;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct ModWavePlan(IReadOnlyList<IReadOnlyList<string>> Waves) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Waves is null;
}
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct StagedMod(
	ModSource Source,
	ModManifest Manifest,
	string StagedRoot,
	ReloadGeneration Generation,
	string? EntryAssemblyPath
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Manifest is null;
}

internal interface ILoadedMod : IStrongRefDroppable {
	StagedMod Staged { get; }
	UntypedBoundedScopeImpl Scope { get; }
}

internal sealed class LoadedContentMod : ILoadedMod {
	public required StagedMod Staged { get; init; }
	public required UntypedBoundedScopeImpl Scope {
		get => field ?? throw new InternalStateException("mod active owner scope strong ref has already been dropped");
		set;
	}

	public void DropStrongReferences() {
		Scope = null!;
	}
}

internal interface ILoadedCodeMod : ILoadedMod {
	ModAlc Alc { get; }
	Assembly Assembly { get; }
	object Entrypoint { get; }
	object? ReloadEntrypoint { get; }
	Type LifetimeIdentityType { get; }
	UntypedBoundedScopeImpl? ActivationScope { get; }
	DetourDeclSet LoadDetours { get; }
	PatchDeclSet LoadPatches { get; }
	DetourDeclSet LinkDetours { get; }
	PatchDeclSet LinkPatches { get; }
	UntypedModExportTable Exports { get; }
	bool Active { get; }
}

internal sealed class LoadedCodeMod<TGameApi> : ILoadedCodeMod {
	public required StagedMod Staged { get; init; }
	public required ModAlc Alc {
		get => field ?? throw new InternalStateException("mod ALC strong ref has already been dropped");
		set;
	}
	public required Assembly Assembly {
		get => field ?? throw new InternalStateException("mod assembly strong ref has already been dropped");
		set;
	}
	public required object Entrypoint {
		get => field ?? throw new InternalStateException("mod entrypoint strong ref has already been dropped");
		set;
	}
	private bool reloadEntrypointDropped = false;
	public required object? ReloadEntrypoint {
		get => !reloadEntrypointDropped ? field : throw new InternalStateException("mod reload entrypoint strong ref has already been dropped");
		set;
	}
	public required Type LifetimeIdentityType {
		get => field ?? throw new InternalStateException("mod lifetime identity type strong ref has already been dropped");
		set;
	}
	public required UntypedBoundedScopeImpl Scope {
		get => field ?? throw new InternalStateException("mod owner scope strong ref has already been dropped");
		set;
	}
	private bool activationScopeDropped = false;
	public UntypedBoundedScopeImpl? ActivationScope {
		get => !activationScopeDropped ? field : throw new InternalStateException("mod activation scope strong ref has already been dropped");
		set;
	}
	private DetourDeclSet? loadDetours;
	public required DetourDeclSet LoadDetours {
		get => loadDetours ?? throw new InternalStateException("mod load detour set strong ref has already been dropped");
		set => loadDetours = value;
	}
	private PatchDeclSet? loadPatches;
	public required PatchDeclSet LoadPatches {
		get => loadPatches ?? throw new InternalStateException("mod load patch set strong ref has already been dropped");
		set => loadPatches = value;
	}
	private DetourDeclSet? linkDetours;
	public required DetourDeclSet LinkDetours {
		get => linkDetours ?? throw new InternalStateException("mod link detour set strong ref has already been dropped");
		set => linkDetours = value;
	}
	private PatchDeclSet? linkPatches;
	public required PatchDeclSet LinkPatches {
		get => linkPatches ?? throw new InternalStateException("mod link patch set strong ref has already been dropped");
		set => linkPatches = value;
	}
	private UntypedModExportTable? exportsBacking;
	public required UntypedModExportTable Exports {
		get => exportsBacking ?? throw new InternalStateException("mod export table strong ref has already been dropped");
		set => exportsBacking = value;
	}
	public bool Active { get; set; }

	public void DropStrongReferences() {
		Alc = null!;
		Assembly = null!;
		Entrypoint = null!;
		reloadEntrypointDropped = true;
		ReloadEntrypoint = null;
		LifetimeIdentityType = null!;
		Scope = null!;
		activationScopeDropped = true;
		ActivationScope = null!;
		loadDetours?.DropStrongReferences();
		loadDetours = null;
		loadPatches?.DropStrongReferences();
		loadPatches = null;
		linkDetours?.DropStrongReferences();
		linkDetours = null;
		linkPatches?.DropStrongReferences();
		linkPatches = null;
		exportsBacking?.DropStrongReferences();
		exportsBacking = null;
	}
}

internal enum ModOperationResultKind {
	Succeeded,
	RollbackSucceeded,
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct ModOperationResult(
	ModOperationResultKind Kind,
	IReadOnlySet<string> ReloadedOwners,
	IReadOnlySet<string> EnabledOwners,
	IReadOnlySet<string> DisabledOwners,
	IReadOnlySet<string> UnloadedOwners,
	ExceptionSnapshot? Failure,
	IReadOnlyList<PendingAlcUnload> PendingUnloads
) : IHasInvalidDefault {
	public static ModOperationResult Succeeded(
		IReadOnlySet<string> reloadedOwners,
		IReadOnlySet<string> enabledOwners,
		IReadOnlySet<string> disabledOwners,
		IReadOnlySet<string> unloadedOwners,
		IReadOnlyList<PendingAlcUnload> pendingUnloads
	) => new(
		ModOperationResultKind.Succeeded,
		reloadedOwners,
		enabledOwners,
		disabledOwners,
		unloadedOwners,
		null,
		pendingUnloads
	);

	public static ModOperationResult RollbackSucceeded(
		ExceptionSnapshot failure,
		IReadOnlyList<PendingAlcUnload> pendingUnloads
	) => new(
		ModOperationResultKind.RollbackSucceeded,
		ReloadedOwners: FrozenSet<string>.Empty,
		EnabledOwners: FrozenSet<string>.Empty,
		DisabledOwners: FrozenSet<string>.Empty,
		UnloadedOwners: FrozenSet<string>.Empty,
		failure,
		pendingUnloads
	);

	bool IHasInvalidDefault.IsDefault => ReloadedOwners is null;
}
