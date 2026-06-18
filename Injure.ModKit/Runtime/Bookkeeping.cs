// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reflection;

using Injure.ModKit.Abstractions;
using Injure.ModKit.Loader;
using Injure.ModKit.MonoMod;

namespace Injure.ModKit.Runtime;

internal readonly record struct ModSource(string RootDirectory, string ManifestPath);
internal readonly record struct DiscoveredMod(ModSource Source, ModManifest Manifest);
internal readonly record struct ResolvedMod(ModManifest Manifest, ModSource Source);

internal readonly record struct ResolvedModGraph(
	IReadOnlyDictionary<string, ResolvedMod> Mods,
	IReadOnlyDictionary<string, string[]> OutgoingOrderEdges,
	IReadOnlyDictionary<string, string[]> ReloadDependentsByTarget,
	ModWavePlan Waves,
	IReadOnlyList<ResolvedMod> ModsInDeterministicOrder
);

internal readonly record struct ModWavePlan(IReadOnlyList<IReadOnlyList<string>> Waves);
internal readonly record struct StagedMod(ModSource Source, ModManifest Manifest, string StagedRoot, ReloadGeneration Generation, string? MainAssemblyPath);

internal sealed class LoadedContentMod : IStrongRefDroppable {
	public required StagedMod Staged { get; init; }
	public required UntypedBoundedScope Scope {
		get => field ?? throw new InternalStateException("mod active owner scope strong ref has already been dropped");
		set;
	}

	public void DropStrongReferences() {
		Scope = null!;
	}
}

internal sealed class LoadedCodeMod<TGameApi> : IStrongRefDroppable {
	public required StagedMod Staged { get; init; }
	public required ModAlc AssemblyLoadContext {
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
	public required UntypedBoundedScope Scope {
		get => field ?? throw new InternalStateException("mod owner scope strong ref has already been dropped");
		set;
	}
	private bool activationScopeDropped = false;
	public UntypedBoundedScope? ActivationScope {
		get => !activationScopeDropped ? field : throw new InternalStateException("mod activation scope strong ref has already been dropped");
		set;
	}
	private GenerationPatchSet? loadHooksBacking;
	public required GenerationPatchSet LoadHooks {
		get => loadHooksBacking ?? throw new InternalStateException("mod patch declaration set strong ref has already been dropped");
		set => loadHooksBacking = value;
	}
	public bool Active { get; set; }

	public void DropStrongReferences() {
		AssemblyLoadContext = null!;
		Assembly = null!;
		Entrypoint = null!;
		reloadEntrypointDropped = true;
		ReloadEntrypoint = null;
		LifetimeIdentityType = null!;
		Scope = null!;
		activationScopeDropped = true;
		ActivationScope = null!;
		loadHooksBacking?.DropStrongReferences();
		loadHooksBacking = null;
	}
}

internal sealed class PendingAlcUnload(ReloadGeneration generation, ModAlc alc) : IStrongRefDroppable {
	public ReloadGeneration Generation { get; } = generation;
	public ModAlc Alc {
		get => field ?? throw new InternalStateException("mod ALC strong ref has already been dropped");
		private set;
	} = alc;

	public void DropStrongReferences() {
		Alc = null!;
	}
}

internal enum ModOperationResultKind {
	Succeeded,
	RollbackSucceeded,
}

internal readonly record struct ModOperationResult(
	ModOperationResultKind Kind,
	IReadOnlySet<string> ReloadedOwners,
	IReadOnlySet<string> EnabledOwners,
	IReadOnlySet<string> DisabledOwners,
	IReadOnlySet<string> UnloadedOwners,
	ExceptionSnapshot? Failure,
	IReadOnlyList<PendingAlcUnload> PendingUnloads
) {
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
}
