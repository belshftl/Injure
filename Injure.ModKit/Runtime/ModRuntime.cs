// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Injure.Core;
using Injure.Internals.Analyzers.Attributes;
using Injure.ModKit.Abstractions;
using Injure.ModKit.Abstractions.ManifestReader;
using Injure.ModKit.Loader;
using Injure.ModKit.MonoMod;

namespace Injure.ModKit.Runtime;

public readonly struct ModApiFactoryContext {
	internal ModApiFactoryContext(string forOwnerID, IUntypedBoundedScope ownerScope) {
		ForOwnerID = forOwnerID;
		OwnerScope = ownerScope;
	}

	public string ForOwnerID { get; }
	public IUntypedBoundedScope OwnerScope { get; }
}

public sealed record ModRuntimeOptions<TGameApi> {
	public required string GameOwnerID { get; init; }
	public required string ModDirectory { get; init; }
	public required string CacheDirectory { get; init; }
	public required Func<ModApiFactoryContext, TGameApi> ApiFactory { get; init; }
	public required IReadOnlyList<string> SharedAssemblies { get; init; }

	public IDiagnosticsSink DiagnosticsSink { get; init; } = new DefaultDiagnosticsSink();
	public TimeSpan UnloadGracePeriod { get; init; } = TimeSpan.FromMilliseconds(75);
	public int MaxLoadParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
	public int MaxScopeTeardownParallelism { get; init; } = 8;
}

public readonly record struct ModWatchSpec(
	string OwnerID,
	bool Reloadable,
	string ManifestPath,
	string? EntryAssemblyPath
);

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct RuntimePhase {
	public enum Case {
		Empty = 1,
		Discovered,
		Resolved,
		Staged,
		NativeLibrariesResolved,
		CodeLoaded,
		HooksDiscovered,
		Loaded,
		LoadHooksApplied,
		Linked,
		//LinkHooksApplied,
		GameAttached,
		Active,
		Shutdown,
		Faulted,
		Aborted,
	}
}

public sealed class ModRuntime<TGameApi> {
	// ==========================================================================
	// bookkeeping
	private readonly struct ContentLifetimeIdentity : IModLifetimeIdentity {
	}

	private readonly record struct ActiveDependent(string OwnerID, bool IsHard);

	private sealed class BoundaryPlan {
		public required ReloadBoundaryKind Boundary { get; init; }
		public required ReloadRequestKind ReloadKind { get; init; }
		public required bool HasExplicitLiveRequest { get; init; }
		public required HashSet<string> CandidateEnabledOwners { get; init; }
		public required HashSet<string> DisableSet { get; init; }
		public required HashSet<string> EnableSet { get; init; }
		public required HashSet<string> ReloadRoots { get; init; }
		public required HashSet<string> ReloadSet { get; init; }

		public bool IsStructural => DisableSet.Count != 0 || EnableSet.Count != 0;
		public bool IsNoOp => DisableSet.Count == 0 && EnableSet.Count == 0 && ReloadSet.Count == 0;

		public HashSet<string> PrepareSet {
			get {
				HashSet<string> result = new(EnableSet, StringComparer.Ordinal);
				foreach (string id in ReloadSet)
					result.Add(id);
				return result;
			}
		}

		public HashSet<string> OldTouchedSet {
			get {
				HashSet<string> result = new(DisableSet, StringComparer.Ordinal);
				foreach (string id in ReloadSet)
					result.Add(id);
				return result;
			}
		}
	}

	private sealed class Transaction {
		public required BoundaryPlan Plan { get; init; }
		public required Dictionary<string, DiscoveredMod> CandidateDiscovered { get; init; }
		public required ResolvedModGraph CandidateGraph { get; init; }
		public required IReadOnlyList<StagedMod> ReplacementStaged { get; init; }
		public required Dictionary<string, LoadedCodeMod<TGameApi>> OldCode { get; init; }
		public required Dictionary<string, LoadedContentMod> OldContent { get; init; }
		public required Dictionary<string, LoadedCodeMod<TGameApi>> PreparedCode { get; init; }
		public required Dictionary<string, LoadedContentMod> PreparedContent { get; init; }

		public void DropPreparedStrongReferences() {
			foreach (LoadedCodeMod<TGameApi> mod in PreparedCode.Values)
				mod.DropStrongReferences();
			foreach (LoadedContentMod mod in PreparedContent.Values)
				mod.DropStrongReferences();
			PreparedCode.Clear();
			PreparedContent.Clear();
		}

		public void DropContainersOnly() {
			OldCode.Clear();
			OldContent.Clear();
			PreparedCode.Clear();
			PreparedContent.Clear();
		}
	}

	private enum OpKind {
		Reload,
		Disable,
		Enable,
	}

	private readonly record struct PendingOp(
		ulong Seq,
		OpKind Kind,
		string OwnerID,
		ReloadRequestKind? ReloadKind = null,
		DisableRequestKind? DisableKind = null,
		EnableRequestKind? EnableKind = null
	);

	private enum ReloadBoundaryKind {
		Safe,
		Live,
	}

	private enum BoundaryPlanReadiness {
		Ready,
		WaitForStrongerBoundary,
	}

	// ==========================================================================
	// constants
	public const string ManifestJson = "manifest.json";
	public const int MaxAlcUnloadGcAttempts = 8;

	// ==========================================================================
	// state
	private readonly string gameOwnerID;
	private readonly string modDir;
	private readonly string cacheDir;
	private readonly Func<ModApiFactoryContext, TGameApi> apiFactory;
	private readonly IReadOnlyList<string> sharedAssemblies;
	private readonly DiagnosticsSinkRegistry diagnosticsSinkRegistry;
	private readonly TimeSpan unloadGracePeriod;
	private readonly int maxLoadParallelism;
	private readonly int maxScopeTeardownParallelism;
	private readonly SemaphoreSlim codeLoadSem;
	private readonly SemaphoreSlim writeLock = new(1, 1);

	private RuntimePhase phase = RuntimePhase.Empty;
	private Dictionary<string, DiscoveredMod> discovered = new();
	private ResolvedModGraph activeGraph;
	private IReadOnlyList<StagedMod> staged = Array.Empty<StagedMod>();
	private readonly NativeLibraryRegistry nativeLibs = new(RuntimeInformation.RuntimeIdentifier);
	private readonly Dictionary<string, LoadedCodeMod<TGameApi>> activeCode = new(StringComparer.Ordinal);
	private readonly Dictionary<string, LoadedContentMod> activeContent = new(StringComparer.Ordinal);
	private GameServices? attachedGameServices;
	private readonly HashSet<string> enabledOwners = new(StringComparer.Ordinal);
	private readonly HookTargetResolver hookTargetResolver = new(AssemblyLoadContext.Default.Assemblies);
	private readonly Dictionary<string, ulong> nextGenerationByOwner = new(StringComparer.Ordinal);

	private readonly Lock opLock = new();
	private readonly List<PendingOp> pendingOps = new();
	private ulong nextOpSeq = 0;

	private readonly OwnerDiagnostics diagnostics;

	// ==========================================================================
	// public properties
	public RuntimePhase CurrentPhase => phase;
	public IOwnerDiagnostics GameDiagnostics { get; }

	public ModRuntime(ModRuntimeOptions<TGameApi> options) {
		gameOwnerID = options.GameOwnerID ?? throw new ArgumentNullException(nameof(options), "GameOwnerID cannot be null");
		modDir = options.ModDirectory ?? throw new ArgumentNullException(nameof(options), "ModDirectory cannot be null");
		cacheDir = options.CacheDirectory ?? throw new ArgumentNullException(nameof(options), "CacheDirectory cannot be null");
		apiFactory = options.ApiFactory ?? throw new ArgumentNullException(nameof(options), "ApiFactory cannot be null");
		sharedAssemblies = options.SharedAssemblies ?? throw new ArgumentNullException(nameof(options), "SharedAssemblies cannot be null");
		if (options.DiagnosticsSink is null)
			throw new ArgumentNullException(nameof(options), "DiagnosticsSink cannot be null");
		diagnosticsSinkRegistry = new DiagnosticsSinkRegistry([options.DiagnosticsSink]);
		unloadGracePeriod = options.UnloadGracePeriod;
		maxLoadParallelism = Math.Max(1, options.MaxLoadParallelism);
		maxScopeTeardownParallelism = Math.Max(1, options.MaxScopeTeardownParallelism);
		codeLoadSem = new SemaphoreSlim(maxLoadParallelism, maxLoadParallelism);
		diagnostics = new OwnerDiagnostics(EngineInfo.OwnerID, diagnosticsSinkRegistry, null);
		GameDiagnostics = new OwnerDiagnostics(gameOwnerID, diagnosticsSinkRegistry, null);
	}

	// ==========================================================================
	// convenience api
	public async ValueTask StartAsync(CancellationToken ct) {
		await DiscoverAsync(ct).ConfigureAwait(false);
		await ResolveAsync(ct).ConfigureAwait(false);
		await StageAsync(ct).ConfigureAwait(false);
		await ResolveNativeLibrariesAsync(ct).ConfigureAwait(false);
		await LoadCodeAsync(ct).ConfigureAwait(false);
		await DiscoverHooksAsync(ct).ConfigureAwait(false);
		await LoadAsync(ct).ConfigureAwait(false);
		await ApplyLoadHooksAsync(ct).ConfigureAwait(false);
		await LinkAsync(ct).ConfigureAwait(false);
	}

	public async ValueTask AttachGameActivateAsync(GameServices gameServices, CancellationToken ct = default) {
		await AttachGameAsync(gameServices, ct).ConfigureAwait(false);
		await ActivateAsync(ct).ConfigureAwait(false);
	}

	public async ValueTask DetachGameDeactivateAsync(CancellationToken ct = default) {
		if (phase == RuntimePhase.Active)
			await DeactivateAsync(ct).ConfigureAwait(false);
		await DetachGameAsync(ct).ConfigureAwait(false);
	}

	public async ValueTask ShutdownOrAbortAsync(CancellationToken ct = default) {
		if (phase == RuntimePhase.Faulted) {
			await AbortAsync().ConfigureAwait(false);
			return;
		}
		try {
			await ShutdownAsync(ct).ConfigureAwait(false);
		} catch (Exception ex) {
			diagnostics.Warning($"normal mod runtime shutdown failed, attempting abort: {ex}");
			await AbortAsync().ConfigureAwait(false);
		}
	}

	public void AttachGameActivateBlocking(GameServices gameServices, CancellationToken ct = default) => block(AttachGameActivateAsync(gameServices, ct));
	public void DetachGameDeactivateBlocking(CancellationToken ct = default) => block(DetachGameDeactivateAsync(ct));
	public void ShutdownOrAbortBlocking(CancellationToken ct = default) => block(ShutdownOrAbortAsync(ct));

	// ==========================================================================
	// phase api
	public async ValueTask DiscoverAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Empty, nameof(DiscoverAsync));
		try {
			Dictionary<string, DiscoveredMod> result = new();
			try {
				Directory.CreateDirectory(modDir);
			} catch {
				// swallow
			}
			foreach (string manifestPath in enumerateManifests(modDir)) {
				ct.ThrowIfCancellationRequested();
				string manifestText = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
				SourceText manifestSource = new(manifestPath, manifestText);
				try {
					ModManifest manifest = ManifestReader.Parse(manifestSource);
					string root = Path.GetDirectoryName(manifestPath) ??
						throw new InternalStateException("was expecting enumerateManifests yielded path to have a dirname");
					result.Add(manifest.OwnerID, new DiscoveredMod(new ModSource(root, manifestPath), manifest));
				} catch (ManifestReadException ex) {
					diagnostics.Error("error parsing mod manifest:\n" + manifestSource.FormatDiagnostic(ex) + "\nskipping this mod");
				}
			}
			discovered = result;
			enabledOwners.Clear();

			ILookup<string, DiscoveredMod> groups = discovered.Values.ToLookup(static m => m.Manifest.OwnerID, StringComparer.Ordinal);
			foreach (IGrouping<string, DiscoveredMod> g in groups) {
				DiscoveredMod[] items = g.ToArray();
				if (items.Length == 1) {
					ref readonly DiscoveredMod mod = ref items[0];
					if (mod.Manifest.OwnerID == EngineInfo.OwnerID)
						diagnostics.Error(
							$"mod manifest '{mod.Source.ManifestPath}' claims to have owner ID '{EngineInfo.OwnerID}', which is reserved for the engine; skipping it"
						);
					else if (mod.Manifest.OwnerID == gameOwnerID)
						diagnostics.Error(
							$"mod manifest '{mod.Source.ManifestPath}' claims to have owner ID '{gameOwnerID}', which is already used by the game; skipping it"
						);
					else
						enabledOwners.Add(mod.Manifest.OwnerID);
				} else {
					StringBuilder sb = new($"the following mod manifests all claim to have the same owner ID ({g.Key}):\n");
					foreach (DiscoveredMod mod in items)
						sb.Append("\t- ").AppendLine(mod.Source.ManifestPath);
					sb.AppendLine("skipping all of them");
					diagnostics.Error(sb.ToString());
				}
			}
			phase = RuntimePhase.Discovered;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public ValueTask ResolveAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Discovered, nameof(ResolveAsync));
		ct.ThrowIfCancellationRequested();
		try {
			activeGraph = ModRelationshipResolver.Resolve(discovered.Values.Where(mod => enabledOwners.Contains(mod.Manifest.OwnerID)).ToArray());
			phase = RuntimePhase.Resolved;
			return ValueTask.CompletedTask;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public async ValueTask StageAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Resolved, nameof(StageAsync));
		try {
			ResolvedModGraph graph = activeGraph;
			List<StagedMod> result = new();
			foreach (ResolvedMod mod in graph.ModsInDeterministicOrder) {
				ct.ThrowIfCancellationRequested();
				result.Add(await stageOneAsync(mod.Source, mod.Manifest, ct).ConfigureAwait(false));
			}
			staged = result;
			phase = RuntimePhase.Staged;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public ValueTask ResolveNativeLibrariesAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Staged, nameof(ResolveNativeLibrariesAsync));
		ct.ThrowIfCancellationRequested();
		try {
			nativeLibs.Rebuild(staged, activeGraph);
			phase = RuntimePhase.NativeLibrariesResolved;
			return ValueTask.CompletedTask;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public async ValueTask LoadCodeAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.NativeLibrariesResolved, nameof(LoadCodeAsync));
		try {
			foreach (StagedMod mod in staged) {
				ct.ThrowIfCancellationRequested();
				if (mod.Manifest is CodeModManifest) {
					LoadedCodeMod<TGameApi> loaded = await loadCodeModBoundedAsync(mod, ct).ConfigureAwait(false);
					activeCode.Add(mod.Manifest.OwnerID, loaded);
				} else if (mod.Manifest is ContentModManifest) {
					activeContent.Add(mod.Manifest.OwnerID, createLoadedContentMod(mod));
				}
			}
			phase = RuntimePhase.CodeLoaded;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public async ValueTask DiscoverHooksAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.CodeLoaded, nameof(DiscoverHooksAsync));
		ct.ThrowIfCancellationRequested();
		try {
			foreach (LoadedCodeMod<TGameApi> mod in activeCode.Values)
				HookDiscoverer<TGameApi>.DiscoverLoadHooks(mod, hookTargetResolver);
			phase = RuntimePhase.HooksDiscovered;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public async ValueTask LoadAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.HooksDiscovered, nameof(LoadAsync));
		ct.ThrowIfCancellationRequested();
		try {
			await Parallel.ForEachAsync(
				activeCode.Values,
				new ParallelOptions {
					MaxDegreeOfParallelism = maxLoadParallelism,
					CancellationToken = ct,
				},
				runLoadAsync
			).ConfigureAwait(false);
			phase = RuntimePhase.Loaded;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public async ValueTask ApplyLoadHooksAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Loaded, nameof(ApplyLoadHooksAsync));
		ct.ThrowIfCancellationRequested();
		try {
			await HookApplier<TGameApi>.ApplyLoadHooksAsync(activeCode.Values.ToArray(), maxLoadParallelism, ct).ConfigureAwait(false);
			phase = RuntimePhase.LoadHooksApplied;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public async ValueTask LinkAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.LoadHooksApplied, nameof(LinkAsync));
		try {
			Dictionary<string, LoadedDependencyInfo>? owners = buildOwnerInfo(staged);
			Dictionary<string, LoadedCodeDependencyInfo>? codeOwners = buildCodeOwnerInfo(activeCode.Values);
			try {
				foreach (IReadOnlyList<string> wave in activeGraph.Waves.Waves) {
					ct.ThrowIfCancellationRequested();
					await Parallel.ForEachAsync(
						wave,
						new ParallelOptions {
							MaxDegreeOfParallelism = maxLoadParallelism,
							CancellationToken = ct,
						},
						async (id, token) => {
							if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod)) {
								nativeLibs.SetPhase(mod.Staged.Generation, NativeImportPhase.LinkOrLater);
								await runLinkAsync(mod, owners, codeOwners, token).ConfigureAwait(false);
							}
						}
					).ConfigureAwait(false);
				}
			} finally {
				owners?.Clear();
				owners = null;
				codeOwners?.Clear();
				codeOwners = null;
			}
			phase = RuntimePhase.Linked;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		}
	}

	public async ValueTask AttachGameAsync(GameServices gameServices, CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(gameServices);
		requirePhase(RuntimePhase.Linked, nameof(AttachGameAsync));
		await writeLock.WaitAsync(ct).ConfigureAwait(false);
		try {
			requirePhase(RuntimePhase.Linked, nameof(AttachGameAsync));
			if (attachedGameServices is not null)
				throw new InvalidOperationException("game services are already attached");
			ct.ThrowIfCancellationRequested();
			attachedGameServices = gameServices;
			phase = RuntimePhase.GameAttached;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		} finally {
			writeLock.Release();
		}
	}

	public async ValueTask ActivateAsync(CancellationToken ct = default) {
		requirePhase(RuntimePhase.GameAttached, nameof(ActivateAsync));
		await writeLock.WaitAsync(ct).ConfigureAwait(false);
		try {
			requirePhase(RuntimePhase.GameAttached, nameof(ActivateAsync));
			GameServices gameServices = attachedGameServices ?? throw new InternalStateException("expected nonnull attachedGameServices with phase at GameAttached");
			ct.ThrowIfCancellationRequested();
			await activateSetAsync(activeCode.Keys.ToHashSet(), activeGraph, activeCode, gameServices, ct).ConfigureAwait(false);
			phase = RuntimePhase.Active;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		} finally {
			writeLock.Release();
		}
	}

	public async ValueTask DeactivateAsync(CancellationToken ct = default) {
		requirePhase(RuntimePhase.Active, nameof(DeactivateAsync));
		await writeLock.WaitAsync(ct).ConfigureAwait(false);
		try {
			requirePhase(RuntimePhase.Active, nameof(DeactivateAsync));
			ct.ThrowIfCancellationRequested();
			await deactivateSetAsync(activeCode.Keys.ToHashSet(), activeGraph, reverse: true, ct).ConfigureAwait(false);
			phase = RuntimePhase.GameAttached;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		} finally {
			writeLock.Release();
		}
	}

	public async ValueTask DetachGameAsync(CancellationToken ct = default) {
		requirePhase(RuntimePhase.GameAttached, nameof(DetachGameAsync));
		await writeLock.WaitAsync(ct).ConfigureAwait(false);
		try {
			requirePhase(RuntimePhase.GameAttached, nameof(DetachGameAsync));
			if (attachedGameServices is null)
				return;
			ct.ThrowIfCancellationRequested();
			attachedGameServices = null;
			phase = RuntimePhase.Linked;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		} finally {
			writeLock.Release();
		}
	}

	public void AttachGameBlocking(GameServices gameServices, CancellationToken ct = default) => block(AttachGameAsync(gameServices, ct));
	public void ActivateBlocking(CancellationToken ct = default) => block(ActivateAsync(ct));
	public void DeactivateBlocking(CancellationToken ct = default) => block(DeactivateAsync(ct));
	public void DetachGameBlocking(CancellationToken ct = default) => block(DetachGameAsync(ct));

	// ==========================================================================
	// mod management
	public void RequestReload(string ownerID) => RequestReload(ownerID, ReloadRequestKind.Any);

	public void RequestReload(string ownerID, ReloadRequestKind kind) {
		requirePhase(RuntimePhase.Active, nameof(RequestReload));
		if (!activeGraph.Mods.TryGetValue(ownerID, out ResolvedMod mod))
			throw new InvalidOperationException($"unknown mod '{ownerID}'");
		if (!mod.Manifest.Reloadable)
			throw new InvalidOperationException($"cannot reload non-reloadable mod '{ownerID}'");
		lock (opLock)
			pendingOps.Add(new PendingOp(Seq: ++nextOpSeq, Kind: OpKind.Reload, OwnerID: ownerID, ReloadKind: kind));
	}

	public void RequestDisable(string ownerID, DisableRequestKind kind) {
		requirePhase(RuntimePhase.Active, nameof(RequestDisable));
		if (!activeGraph.Mods.TryGetValue(ownerID, out ResolvedMod mod))
			throw new InvalidOperationException($"unknown mod '{ownerID}'");
		if (!mod.Manifest.Reloadable)
			throw new InvalidOperationException($"cannot enable/disable non-reloadable mod '{ownerID}'");
		lock (opLock)
			pendingOps.Add(new PendingOp(Seq: ++nextOpSeq, Kind: OpKind.Disable, OwnerID: ownerID, DisableKind: kind));
	}

	public void RequestEnable(string ownerID, EnableRequestKind kind) {
		requirePhase(RuntimePhase.Active, nameof(RequestEnable));
		if (!discovered.TryGetValue(ownerID, out DiscoveredMod mod))
			throw new InvalidOperationException($"unknown mod '{ownerID}'");
		if (!mod.Manifest.Reloadable)
			throw new InvalidOperationException($"cannot enable/disable non-reloadable mod '{ownerID}'");
		lock (opLock)
			pendingOps.Add(new PendingOp(Seq: ++nextOpSeq, Kind: OpKind.Enable, OwnerID: ownerID, EnableKind: kind));
	}

	public void AtSafeBoundaryBlocking(CancellationToken ct = default) => block(processBoundaryAsync(ReloadBoundaryKind.Safe, ct));
	public void AtLiveBoundaryBlocking(CancellationToken ct = default) => block(processBoundaryAsync(ReloadBoundaryKind.Live, ct));
	public ValueTask AtSafeBoundaryAsync(CancellationToken ct = default) => processBoundaryAsync(ReloadBoundaryKind.Safe, ct);
	public ValueTask AtLiveBoundaryAsync(CancellationToken ct = default) => processBoundaryAsync(ReloadBoundaryKind.Live, ct);

	public ModWatchSpec[] GetWatchSpecs() =>
		discovered.Values.Select(static mod => new ModWatchSpec(
				mod.Manifest.OwnerID,
				mod.Manifest.Reloadable,
				mod.Source.ManifestPath,
				mod.Manifest is CodeModManifest code ? Path.Combine(mod.Source.RootDirectory, code.EntryAssembly) : null
			)
		).ToArray();

	// ==========================================================================
	// termination
	public async ValueTask ShutdownAsync(CancellationToken ct = default) {
		await writeLock.WaitAsync(ct).ConfigureAwait(false);
		try {
			if (phase == RuntimePhase.Empty || phase == RuntimePhase.Shutdown) {
				phase = RuntimePhase.Shutdown;
				return;
			}
			if (phase == RuntimePhase.Faulted)
				throw new InvalidOperationException("runtime is faulted; use AbortAsync() for emergency cleanup");
			if (phase == RuntimePhase.Aborted)
				throw new InvalidOperationException("runtime has already been aborted");
			lock (opLock)
				pendingOps.Clear();

			RuntimePhase startingPhase = phase;
			if (startingPhase == RuntimePhase.Active) {
				await deactivateSetAsync(activeCode.Keys.ToHashSet(StringComparer.Ordinal), activeGraph, reverse: true, ct).ConfigureAwait(false);
				phase = RuntimePhase.GameAttached;
			}

			List<PendingAlcUnload> pendingUnloads = new();
			if (shutdownShouldCallUnload(startingPhase))
				await unloadAllCodeGenerationsAsync(ct).ConfigureAwait(false);
			if (shutdownHasOwnerScopes(startingPhase)) {
				foreach (LoadedCodeMod<TGameApi> mod in activeCode.Values) {
					await invalidateScopesAsync(mod, ReloadTeardownReason.Shutdown, ct).ConfigureAwait(false);
					pendingUnloads.Add(detachForUnload(mod));
				}
				foreach (LoadedContentMod mod in activeContent.Values)
					await mod.Scope.InvalidateAsync(ReloadTeardownReason.Shutdown, ct).ConfigureAwait(false);
			}

			clearRuntimeStateAfterShutdown();
			if (unloadGracePeriod > TimeSpan.Zero && pendingUnloads.Count != 0)
				await Task.Delay(unloadGracePeriod, CancellationToken.None).ConfigureAwait(false);
			foreach (PendingAlcUnload pending in pendingUnloads)
				unload(pending, diagnostics);

			nextGenerationByOwner.Clear();

			phase = RuntimePhase.Shutdown;
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		} finally {
			writeLock.Release();
		}
	}

	public async ValueTask AbortAsync() {
		await writeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
		try {
			if (phase == RuntimePhase.Empty || phase == RuntimePhase.Shutdown || phase == RuntimePhase.Aborted)
				return; // finally block should set phase = RuntimePhase.Aborted
			lock (opLock)
				pendingOps.Clear();

			List<PendingAlcUnload> pendingUnloads = new();
			foreach (LoadedCodeMod<TGameApi> mod in activeCode.Values) {
				try {
					await invalidateScopesAsync(mod, ReloadTeardownReason.Abort, CancellationToken.None).ConfigureAwait(false);
				} catch (Exception ex) {
					diagnostics.Warning($"abort: error invalidating scope for '{mod.Staged.Manifest.OwnerID}', moving on: {ex}");
				}
				try {
					pendingUnloads.Add(detachForUnload(mod));
				} catch (Exception ex) {
					diagnostics.Warning($"abort: error detaching ALC for '{mod.Staged.Manifest.OwnerID}', moving on: {ex}");
				}
			}

			foreach (LoadedContentMod mod in activeContent.Values)
				try {
					await mod.Scope.InvalidateAsync(ReloadTeardownReason.Abort, CancellationToken.None).ConfigureAwait(false);
				} catch (Exception ex) {
					diagnostics.Warning($"abort: error invalidating content scope for '{mod.Staged.Manifest.OwnerID}', moving on: {ex}");
				}

			clearRuntimeStateAfterShutdown();
			if (unloadGracePeriod > TimeSpan.Zero && pendingUnloads.Count != 0)
				await Task.Delay(unloadGracePeriod, CancellationToken.None).ConfigureAwait(false);
			foreach (PendingAlcUnload pending in pendingUnloads)
				try {
					unload(pending, diagnostics);
				} catch (Exception ex) {
					diagnostics.Warning($"abort: error unloading '{pending.Generation}', moving on: {ex}");
				}

			nextGenerationByOwner.Clear();
		} finally {
			phase = RuntimePhase.Aborted;
			writeLock.Release();
		}
	}

	public void ShutdownBlocking(CancellationToken ct = default) => block(ShutdownAsync(ct));
	public void AbortBlocking() => block(AbortAsync());

	// ==========================================================================
	// private methods
	private static bool shutdownHasOwnerScopes(RuntimePhase phase) => phase.Tag switch {
		RuntimePhase.Case.CodeLoaded or
			RuntimePhase.Case.HooksDiscovered or
			RuntimePhase.Case.Loaded or
			RuntimePhase.Case.LoadHooksApplied or
			RuntimePhase.Case.Linked or
			RuntimePhase.Case.GameAttached or
			RuntimePhase.Case.Active => true,
		_ => false,
	};

	private static bool shutdownShouldCallUnload(RuntimePhase phase) => phase.Tag switch {
		RuntimePhase.Case.Loaded or
			RuntimePhase.Case.LoadHooksApplied or
			RuntimePhase.Case.Linked or
			RuntimePhase.Case.GameAttached or
			RuntimePhase.Case.Active => true,
		_ => false,
	};

	private void clearRuntimeStateAfterShutdown() {
		discovered = new Dictionary<string, DiscoveredMod>();
		activeGraph = default;
		staged = Array.Empty<StagedMod>();
		activeCode.Clear();
		activeContent.Clear();
		attachedGameServices = null;
		enabledOwners.Clear();
	}

	private static IEnumerable<string> enumerateManifests(string root) {
		// if the root disappears halfway through, just return everything we saw so far
		// might be better to not return anything but that requires enumerating everything upfront

		IEnumerator<string> dirs;
		try {
			dirs = Directory.EnumerateDirectories(root).GetEnumerator();
		} catch (DirectoryNotFoundException) {
			yield break;
		}
		using (dirs) {
			for (;;) {
				string dir;
				try {
					if (!dirs.MoveNext())
						yield break;
					dir = dirs.Current;
				} catch (DirectoryNotFoundException) {
					yield break;
				}
				string manifest = Path.Combine(dir, ManifestJson);
				if (File.Exists(manifest))
					yield return manifest;
			}
		}
	}

	private static void block(ValueTask task) {
		if (task.IsCompletedSuccessfully) {
			task.GetAwaiter().GetResult();
			return;
		}
		task.AsTask().GetAwaiter().GetResult();
	}

	private PendingOp[] peekPendingBatch(ReloadBoundaryKind boundary) {
		lock (opLock) {
			List<PendingOp> batch = new();
			foreach (PendingOp op in pendingOps) {
				if (!isEligibleAtBoundary(op, boundary))
					continue;
				batch.Add(op);
			}
			batch.Sort(static (a, b) => a.Seq.CompareTo(b.Seq));
			return batch.Count == 0 ? Array.Empty<PendingOp>() : batch.ToArray();
		}
	}

	private void removePendingBatch(PendingOp[] batch) {
		if (batch.Length == 0)
			return;

		HashSet<ulong> seqs = new(batch.Select(static op => op.Seq));
		lock (opLock) {
			for (int i = pendingOps.Count - 1; i >= 0; i--)
				if (seqs.Contains(pendingOps[i].Seq))
					pendingOps.RemoveAt(i);
		}
	}

	private async ValueTask processBoundaryAsync(ReloadBoundaryKind boundary, CancellationToken ct) {
		await writeLock.WaitAsync(ct).ConfigureAwait(false);
		try {
			for (;;) {
				PendingOp[] batch = peekPendingBatch(boundary);
				if (batch.Length == 0)
					return;

				BoundaryPlan plan = createBoundaryPlan(batch, boundary);
				if (plan.IsNoOp) {
					removePendingBatch(batch);
					continue;
				}

				BoundaryPlanReadiness readiness = validateReloadCapabilitiesAtBoundary(activeGraph, plan.ReloadSet, boundary, plan.HasExplicitLiveRequest, oldManifest: true);
				if (readiness == BoundaryPlanReadiness.WaitForStrongerBoundary)
					return;

				Transaction? transaction = await prepareTransactionAsync(plan, ct).ConfigureAwait(false);
				if (transaction is null)
					return;

				removePendingBatch(batch);

				ModOperationResult r = await commitTransactionAsync(transaction, ct).ConfigureAwait(false);
				transaction.DropContainersOnly();
				transaction = null;
				if (unloadGracePeriod > TimeSpan.Zero)
					await Task.Delay(unloadGracePeriod, CancellationToken.None).ConfigureAwait(false);
				foreach (PendingAlcUnload pending in r.PendingUnloads)
					unload(pending, diagnostics);
				if (r.Kind == ModOperationResultKind.RollbackSucceeded && r.Failure is {} failure)
					throw failure.ToException();
			}
		} catch {
			phase = RuntimePhase.Faulted;
			throw;
		} finally {
			writeLock.Release();
		}
	}

	private BoundaryPlan createBoundaryPlan(PendingOp[] batch, ReloadBoundaryKind boundary) {
		HashSet<string> candidateEnabled = new(enabledOwners, StringComparer.Ordinal);
		HashSet<string> reloadRoots = new(StringComparer.Ordinal);
		bool hasStructuralOps = false;
		bool hasExplicitLiveRequest = false;
		ReloadRequestKind reloadKind = boundary == ReloadBoundaryKind.Live ? ReloadRequestKind.Live : ReloadRequestKind.SafeBoundary;

		foreach (PendingOp op in batch)
			switch (op.Kind) {
			case OpKind.Reload:
				if (!candidateEnabled.Contains(op.OwnerID))
					break;
				reloadRoots.Add(op.OwnerID);
				if (op.ReloadKind == ReloadRequestKind.Live)
					hasExplicitLiveRequest = true;
				break;
			case OpKind.Disable:
				hasStructuralOps = true;
				DisableRequestKind drk = op.DisableKind ?? throw new InternalStateException("OpKind.Disable didn't set a disable request kind");
				applyDisableToCandidateSet(op.OwnerID, drk, candidateEnabled, reloadRoots);
				break;
			case OpKind.Enable:
				hasStructuralOps = true;
				EnableRequestKind erk = op.EnableKind ?? throw new InternalStateException("OpKind.Enable didn't set an enable request kind");
				applyEnableToCandidateSet(op.OwnerID, erk, candidateEnabled, reloadRoots);
				break;
			}

		if (hasStructuralOps)
			reloadKind = ReloadRequestKind.SafeBoundary;

		HashSet<string> disableSet = new(enabledOwners, StringComparer.Ordinal);
		disableSet.ExceptWith(candidateEnabled);

		HashSet<string> enableSet = new(candidateEnabled, StringComparer.Ordinal);
		enableSet.ExceptWith(enabledOwners);

		reloadRoots.ExceptWith(disableSet);
		reloadRoots.ExceptWith(enableSet);

		HashSet<string> reloadSet = computeFilteredReloadClosure(reloadRoots, candidateEnabled, enableSet, disableSet);
		return new BoundaryPlan {
			Boundary = boundary,
			ReloadKind = reloadKind,
			HasExplicitLiveRequest = hasExplicitLiveRequest,
			CandidateEnabledOwners = candidateEnabled,
			DisableSet = disableSet,
			EnableSet = enableSet,
			ReloadRoots = reloadRoots,
			ReloadSet = reloadSet,
		};
	}

	private void applyDisableToCandidateSet(string ownerID, DisableRequestKind kind, HashSet<string> candidateEnabled, HashSet<string> reloadRoots) {
		if (!candidateEnabled.Contains(ownerID))
			return;

		Queue<string> queue = new();
		queue.Enqueue(ownerID);

		while (queue.Count != 0) {
			string target = queue.Dequeue();
			if (!candidateEnabled.Contains(target))
				continue;

			List<ActiveDependent> dependents = findActiveDependentsTargeting(target, candidateEnabled);
			foreach (ActiveDependent dependent in dependents) {
				if (!candidateEnabled.Contains(dependent.OwnerID))
					continue;

				if (kind == DisableRequestKind.Strict)
					throw new ModLoadException(target, $"cannot disable '{ownerID}' because enabled mod '{dependent.OwnerID}' depends on '{target}'");

				if (dependent.IsHard) {
					candidateEnabled.Remove(dependent.OwnerID);
					queue.Enqueue(dependent.OwnerID);
				} else {
					if (kind == DisableRequestKind.DisableDependents) {
						candidateEnabled.Remove(dependent.OwnerID);
						queue.Enqueue(dependent.OwnerID);
					} else if (kind == DisableRequestKind.DisableDependentsAndReloadOptionalDependents) {
						reloadRoots.Add(dependent.OwnerID);
					}
				}
			}

			candidateEnabled.Remove(target);
		}
	}

	private void applyEnableToCandidateSet(string ownerID, EnableRequestKind kind, HashSet<string> candidateEnabled, HashSet<string> reloadRoots) {
		if (candidateEnabled.Contains(ownerID))
			return;

		if (!discovered.ContainsKey(ownerID))
			throw new ModLoadException(ownerID, $"unknown mod '{ownerID}'");

		bool enableRequiredDependencies = kind.Tag is
			EnableRequestKind.Case.EnableRequiredDependencies or
			EnableRequestKind.Case.EnableRequiredDependenciesAndReloadOptionalDependents;
		HashSet<string> newlyEnabled = computeRequiredEnableClosure(ownerID, enableRequiredDependencies, discovered);
		foreach (string id in newlyEnabled)
			candidateEnabled.Add(id);

		if (kind == EnableRequestKind.EnableRequiredDependenciesAndReloadOptionalDependents)
			foreach (string id in newlyEnabled)
				addOptionalDependentsTargeting(id, candidateEnabled, reloadRoots);
	}

	private List<ActiveDependent> findActiveDependentsTargeting(string targetOwnerID, HashSet<string> candidateEnabled) {
		List<ActiveDependent> result = new();
		foreach (string ownerID in enabledOwners) {
			if (!candidateEnabled.Contains(ownerID))
				continue;

			if (!activeGraph.Mods.TryGetValue(ownerID, out ResolvedMod mod))
				continue;

			foreach (ModRelationshipManifest relationship in mod.Manifest.Relationships) {
				if (!StringComparer.Ordinal.Equals(relationship.OwnerID, targetOwnerID))
					continue;

				switch (relationship.Kind.Tag) {
				case ModRelationshipKind.Case.RequiresSelfAfter:
				case ModRelationshipKind.Case.RequiresSelfBefore:
					result.Add(new ActiveDependent(ownerID, IsHard: true));
					break;
				case ModRelationshipKind.Case.IfPresentSelfAfter:
				case ModRelationshipKind.Case.IfPresentSelfBefore:
					result.Add(new ActiveDependent(ownerID, IsHard: false));
					break;
				}
			}
		}
		return result;
	}

	private HashSet<string> computeRequiredEnableClosure(string rootOwnerID, bool enableRequiredDependencies, Dictionary<string, DiscoveredMod> discoveredMap) {
		HashSet<string> result = new(StringComparer.Ordinal);
		Stack<string> stack = new();

		result.Add(rootOwnerID);
		stack.Push(rootOwnerID);

		while (stack.Count != 0) {
			string ownerID = stack.Pop();

			if (!discoveredMap.TryGetValue(ownerID, out DiscoveredMod mod))
				throw new ModLoadException(ownerID, $"unknown mod '{ownerID}'");

			foreach (ModRelationshipManifest relationship in mod.Manifest.Relationships) {
				if (!(relationship.Kind.Tag is ModRelationshipKind.Case.RequiresSelfAfter or ModRelationshipKind.Case.RequiresSelfBefore))
					continue;

				if (!discoveredMap.ContainsKey(relationship.OwnerID))
					throw new ModLoadException(ownerID, $"required dependency '{relationship.OwnerID}' is missing");

				if (enabledOwners.Contains(relationship.OwnerID) || result.Contains(relationship.OwnerID))
					continue;

				if (!enableRequiredDependencies)
					throw new ModLoadException(ownerID, $"required dependency '{relationship.OwnerID}' is disabled");
				result.Add(relationship.OwnerID);
				stack.Push(relationship.OwnerID);
			}
		}
		return result;
	}

	private void addOptionalDependentsTargeting(string targetOwnerID, HashSet<string> candidateEnabled, HashSet<string> reloadRoots) {
		foreach (string ownerID in enabledOwners) {
			if (!candidateEnabled.Contains(ownerID))
				continue;
			if (!activeGraph.Mods.TryGetValue(ownerID, out ResolvedMod mod))
				continue;
			foreach (ModRelationshipManifest relationship in mod.Manifest.Relationships) {
				if (!StringComparer.Ordinal.Equals(relationship.OwnerID, targetOwnerID))
					continue;
				if (relationship.Kind.Tag is ModRelationshipKind.Case.IfPresentSelfAfter or ModRelationshipKind.Case.IfPresentSelfBefore)
					reloadRoots.Add(ownerID);
			}
		}
	}

	private HashSet<string> computeFilteredReloadClosure(HashSet<string> roots, HashSet<string> candidateEnabled, HashSet<string> enableSet, HashSet<string> disableSet) {
		HashSet<string> result = new(StringComparer.Ordinal);
		Stack<string> stack = new();

		foreach (string root in roots) {
			if (!enabledOwners.Contains(root))
				continue;
			if (!candidateEnabled.Contains(root))
				continue;
			if (enableSet.Contains(root))
				continue;
			if (disableSet.Contains(root))
				continue;
			if (result.Add(root))
				stack.Push(root);
		}

		while (stack.Count != 0) {
			string ownerID = stack.Pop();
			if (!activeGraph.ReloadDependentsByTarget.TryGetValue(ownerID, out string[]? dependents))
				continue;
			foreach (string dependent in dependents) {
				if (!enabledOwners.Contains(dependent))
					continue;
				if (!candidateEnabled.Contains(dependent))
					continue;
				if (enableSet.Contains(dependent))
					continue;
				if (disableSet.Contains(dependent))
					continue;
				if (result.Add(dependent))
					stack.Push(dependent);
			}
		}

		return result;
	}

	private async ValueTask<Transaction?> prepareTransactionAsync(BoundaryPlan plan, CancellationToken ct) {
		HashSet<string> prepareSet = plan.PrepareSet;
		HashSet<string> oldTouchedSet = plan.OldTouchedSet;

		var candidateDiscovered = discovered.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal);
		foreach (string id in prepareSet) {
			if (!candidateDiscovered.TryGetValue(id, out DiscoveredMod old))
				throw new ModLoadException(id, $"unknown mod '{id}'");
			string manifestText = await File.ReadAllTextAsync(old.Source.ManifestPath, ct).ConfigureAwait(false);
			SourceText manifestSource = new(old.Source.ManifestPath, manifestText);
			try {
				ModManifest manifest = ManifestReader.Parse(manifestSource);
				candidateDiscovered[id] = new DiscoveredMod(old.Source, manifest);
			} catch (ManifestReadException ex) {
				throw new ModLoadException(id, "error parsing mod manifest while preparing a mod transaction:\n" + manifestSource.FormatDiagnostic(ex));
			}
		}

		ResolvedModGraph candidateGraph = ModRelationshipResolver.Resolve(
			candidateDiscovered.Values.Where(mod => plan.CandidateEnabledOwners.Contains(mod.Manifest.OwnerID)).ToArray()
		);
		BoundaryPlanReadiness candidateReadiness = validateReloadCapabilitiesAtBoundary(
			candidateGraph,
			plan.ReloadSet,
			plan.Boundary,
			plan.HasExplicitLiveRequest,
			oldManifest: false
		);
		if (candidateReadiness == BoundaryPlanReadiness.WaitForStrongerBoundary)
			return null;

		List<StagedMod> replacementStaged = new();
		foreach (ResolvedMod resolved in candidateGraph.ModsInDeterministicOrder) {
			if (!prepareSet.Contains(resolved.Manifest.OwnerID))
				continue;
			replacementStaged.Add(await stageOneAsync(resolved.Source, resolved.Manifest, ct).ConfigureAwait(false));
		}

		var oldCode = activeCode
			.Where(kvp => oldTouchedSet.Contains(kvp.Key))
			.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal);
		var oldContent = activeContent
			.Where(kvp => oldTouchedSet.Contains(kvp.Key))
			.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal);

		Dictionary<string, LoadedCodeMod<TGameApi>> preparedCode = new(StringComparer.Ordinal);
		Dictionary<string, LoadedContentMod> preparedContent = new(StringComparer.Ordinal);

		ExceptionSnapshot prepareErr;
		try {
			foreach (StagedMod stagedMod in replacementStaged)
				if (stagedMod.Manifest is CodeModManifest) {
					LoadedCodeMod<TGameApi> loaded = await loadCodeModBoundedAsync(stagedMod, ct).ConfigureAwait(false);
					HookDiscoverer<TGameApi>.DiscoverLoadHooks(loaded, hookTargetResolver);
					preparedCode.Add(stagedMod.Manifest.OwnerID, loaded);
				} else if (stagedMod.Manifest is ContentModManifest) {
					preparedContent.Add(stagedMod.Manifest.OwnerID, createLoadedContentMod(stagedMod));
				}

			Dictionary<string, LoadedDependencyInfo> candidateOwners = buildOwnerInfo(
				staged.Where(mod => plan.CandidateEnabledOwners.Contains(mod.Manifest.OwnerID) && !prepareSet.Contains(mod.Manifest.OwnerID)).Concat(replacementStaged)
			);

			Dictionary<string, LoadedCodeDependencyInfo> candidateCodeOwners = buildCodeOwnerInfo(
				activeCode.Values.Where(mod => plan.CandidateEnabledOwners.Contains(mod.Staged.Manifest.OwnerID) && !prepareSet.Contains(mod.Staged.Manifest.OwnerID))
					.Concat(preparedCode.Values)
			);

			await Task.WhenAll(preparedCode.Values.Select(mod => runLoadAsync(mod, ct).AsTask())).ConfigureAwait(false);
			foreach (IReadOnlyList<string> wave in candidateGraph.Waves.Waves) {
				List<Task> tasks = new();
				foreach (string id in wave)
					if (preparedCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod)) {
						nativeLibs.SetPhase(mod.Staged.Generation, NativeImportPhase.LinkOrLater);
						tasks.Add(runLinkAsync(mod, candidateOwners, candidateCodeOwners, ct).AsTask());
					}
				try {
					await Task.WhenAll(tasks).ConfigureAwait(false);
				} finally {
					tasks.Clear();
				}
			}

			return new Transaction {
				Plan = plan,
				CandidateDiscovered = candidateDiscovered,
				CandidateGraph = candidateGraph,
				ReplacementStaged = replacementStaged,
				OldCode = oldCode,
				OldContent = oldContent,
				PreparedCode = preparedCode,
				PreparedContent = preparedContent,
			};
		} catch (Exception ex) when (!ExceptionPolicy.IsInternalState(ex)) {
			prepareErr = ExceptionSnapshot.FromException(ex);
			ex = null!;
		}
		List<ExceptionSnapshot> cleanupErrs = new();
		foreach (LoadedCodeMod<TGameApi> mod in preparedCode.Values)
			try {
				await startDestroyPreparedCodeGenerationAsync(mod, ReloadTeardownReason.FailureRollback, ct).ConfigureAwait(false);
			} catch (Exception cleanupEx) when (!ExceptionPolicy.IsInternalState(cleanupEx)) {
				cleanupErrs.Add(ExceptionSnapshot.FromException(cleanupEx));
			}
		if (unloadGracePeriod > TimeSpan.Zero)
			await Task.Delay(unloadGracePeriod, CancellationToken.None).ConfigureAwait(false);
		foreach (LoadedCodeMod<TGameApi> mod in preparedCode.Values)
			try {
				unload(detachForUnload(mod), diagnostics);
			} catch (Exception cleanupEx) when (!ExceptionPolicy.IsInternalState(cleanupEx)) {
				cleanupErrs.Add(ExceptionSnapshot.FromException(cleanupEx));
			}
		foreach (LoadedContentMod mod in preparedContent.Values)
			try {
				await mod.Scope.InvalidateAsync(ReloadTeardownReason.FailureRollback, ct).ConfigureAwait(false);
			} catch (Exception cleanupEx) when (!ExceptionPolicy.IsInternalState(cleanupEx)) {
				cleanupErrs.Add(ExceptionSnapshot.FromException(cleanupEx));
			}
		if (cleanupErrs.Count > 0)
			throw new AggregateException("mod preparation failed and cleanup also failed", cleanupErrs.Select(static e => e.ToException()).Prepend(prepareErr.ToException()));
		throw prepareErr.ToException();
	}

	private async ValueTask<ModOperationResult> commitTransactionAsync(Transaction transaction, CancellationToken ct) {
		BoundaryPlan plan = transaction.Plan;
		Dictionary<string, ModLiveStateBlob> capturedState = new(StringComparer.Ordinal);
		GameServices? gameServices = attachedGameServices;
		bool wasActive = phase == RuntimePhase.Active;
		bool destructiveBoundaryCrossed = false;
		bool publishBoundaryCrossed = false;
		ExceptionSnapshot reloadErr;
		try {
			var reloadSetSnapshot = plan.ReloadSet.ToFrozenSet(StringComparer.Ordinal);

			if (!plan.IsStructural && plan.ReloadKind == ReloadRequestKind.Live)
				foreach (string id in plan.ReloadSet)
					if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? old) && old.ReloadEntrypoint is not null) {
						object ctx = createReloadContext(old, createApi(old), diagnosticsSinkRegistry, reloadSetSnapshot, gameServices);
						try {
							capturedState.Add(id, await invokeSaveStateAsync(old, ctx, ct).ConfigureAwait(false));
						} finally {
							(ctx as IStrongRefDroppable)?.DropStrongReferences();
							ctx = null!;
						}
					}

			try {
				if (wasActive)
					await deactivateSetAsync(plan.OldTouchedSet, activeGraph, reverse: true, ct).ConfigureAwait(false);
			} finally {
				destructiveBoundaryCrossed = true;
			}

			foreach (LoadedCodeMod<TGameApi> mod in transaction.OldCode.Values)
				await unloadCodeGenerationAsync(mod, ct).ConfigureAwait(false);

			await disposeOldOwnerScopesAsync(plan.OldTouchedSet, plan, ct).ConfigureAwait(false);

			await HookApplier<TGameApi>.ApplyLoadHooksAsync(transaction.PreparedCode.Values.ToArray(), maxLoadParallelism, ct).ConfigureAwait(false);

			if (!plan.IsStructural && plan.ReloadKind == ReloadRequestKind.Live)
				foreach (KeyValuePair<string, ModLiveStateBlob> kvp in capturedState)
					if (transaction.PreparedCode.TryGetValue(kvp.Key, out LoadedCodeMod<TGameApi>? next) && next.ReloadEntrypoint is not null) {
						object ctx = createReloadContext(next, createApi(next), diagnosticsSinkRegistry, reloadSetSnapshot, gameServices);
						try {
							await invokeRestoreStateAsync(next, ctx, kvp.Value, ct).ConfigureAwait(false);
						} finally {
							(ctx as IStrongRefDroppable)?.DropStrongReferences();
							ctx = null!;
						}
					}

			if (wasActive)
				await activateSetAsync(
					plan.PrepareSet,
					transaction.CandidateGraph,
					transaction.PreparedCode,
					gameServices ?? throw new InternalStateException("active runtime has no attached GameServices"),
					ct
				).ConfigureAwait(false);
			publishTransaction(transaction);
			publishBoundaryCrossed = true;

			List<PendingAlcUnload> oldUnloads = new();
			foreach (LoadedCodeMod<TGameApi> mod in transaction.OldCode.Values)
				oldUnloads.Add(detachForUnload(mod));

			return ModOperationResult.Succeeded(
				plan.ReloadSet.ToFrozenSet(StringComparer.Ordinal),
				plan.EnableSet.ToFrozenSet(StringComparer.Ordinal),
				plan.DisableSet.ToFrozenSet(StringComparer.Ordinal),
				plan.OldTouchedSet.ToFrozenSet(StringComparer.Ordinal),
				oldUnloads
			);
		} catch (Exception ex) when (!ExceptionPolicy.IsInternalState(ex)) {
			reloadErr = ExceptionSnapshot.FromException(ex);
			ex = null!;
		}

		if (publishBoundaryCrossed)
			throw reloadErr.ToException(); // post-publish rollback is unsafe and will most likely corrupt state, just don't bother

		if (!destructiveBoundaryCrossed) {
			foreach (LoadedCodeMod<TGameApi> mod in transaction.PreparedCode.Values)
				await startDestroyPreparedCodeGenerationAsync(mod, ReloadTeardownReason.FailureRollback, ct).ConfigureAwait(false);
			if (unloadGracePeriod > TimeSpan.Zero)
				await Task.Delay(unloadGracePeriod, CancellationToken.None).ConfigureAwait(false);
			foreach (LoadedCodeMod<TGameApi> mod in transaction.PreparedCode.Values)
				unload(detachForUnload(mod), diagnostics);
			foreach (LoadedContentMod mod in transaction.PreparedContent.Values)
				await mod.Scope.InvalidateAsync(ReloadTeardownReason.FailureRollback, ct).ConfigureAwait(false);
			throw reloadErr.ToException();
		}

		List<PendingAlcUnload> preparedUnloads = new();
		List<ExceptionSnapshot> rollbackErrs = new();
		try {
			foreach (LoadedCodeMod<TGameApi> mod in transaction.PreparedCode.Values) {
				await startDestroyPreparedCodeGenerationAsync(mod, ReloadTeardownReason.FailureRollback, ct).ConfigureAwait(false);
				preparedUnloads.Add(detachForUnload(mod));
			}
			foreach (LoadedContentMod mod in transaction.PreparedContent.Values)
				await mod.Scope.InvalidateAsync(ReloadTeardownReason.FailureRollback, ct).ConfigureAwait(false);
		} catch (Exception ex) when (!ExceptionPolicy.IsInternalState(ex)) {
			rollbackErrs.Add(ExceptionSnapshot.FromException(ex));
		}

		try {
			foreach (LoadedCodeMod<TGameApi> old in transaction.OldCode.Values)
				old.Scope = new UntypedBoundedScope(old.Staged.Generation, maxScopeTeardownParallelism);
			foreach (LoadedContentMod old in transaction.OldContent.Values)
				old.Scope = new UntypedBoundedScope(old.Staged.Generation, maxScopeTeardownParallelism);

			await HookApplier<TGameApi>.ApplyLoadHooksAsync(transaction.OldCode.Values.ToArray(), maxLoadParallelism, ct).ConfigureAwait(false);
			if (wasActive)
				await activateSetAsync(
					plan.OldTouchedSet,
					activeGraph,
					activeCode,
					gameServices ?? throw new InternalStateException("active runtime has no attached GameServices"),
					ct
				).ConfigureAwait(false);
		} catch (Exception ex) when (!ExceptionPolicy.IsInternalState(ex)) {
			rollbackErrs.Add(ExceptionSnapshot.FromException(ex));
		}

		if (rollbackErrs.Count > 0)
			throw new AggregateException("mod operation failed and rollback also failed", rollbackErrs.Select(static e => e.ToException()).Prepend(reloadErr.ToException()));
		var r = ModOperationResult.RollbackSucceeded(reloadErr, preparedUnloads);
		transaction.DropPreparedStrongReferences();
		return r;
	}

	private ReloadGeneration nextReloadGeneration(string ownerID) {
		ref ulong next = ref CollectionsMarshal.GetValueRefOrAddDefault(nextGenerationByOwner, ownerID, out _);
		checked {
			next++;
		}
		return new ReloadGeneration(ownerID, next);
	}

	private async ValueTask<StagedMod> stageOneAsync(ModSource source, ModManifest manifest, CancellationToken ct) {
		ReloadGeneration generation = nextReloadGeneration(manifest.OwnerID);
		string target = Path.Combine(cacheDir, manifest.OwnerID, generation.Value.ToString("D4"));
		if (Directory.Exists(target))
			Directory.Delete(target, recursive: true);
		Directory.CreateDirectory(target);
		await copyDirectoryAsync(source.RootDirectory, target, ct).ConfigureAwait(false);
		string? assemblyPath = manifest is CodeModManifest code ? Path.Combine(target, code.EntryAssembly) : null;
		return new StagedMod(source, manifest, target, generation, assemblyPath);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private LoadedContentMod createLoadedContentMod(StagedMod stagedMod) => new() {
		Staged = stagedMod,
		Scope = new UntypedBoundedScope(stagedMod.Generation, maxScopeTeardownParallelism),
	};

	private async ValueTask<LoadedCodeMod<TGameApi>> loadCodeModBoundedAsync(StagedMod stagedMod, CancellationToken ct) {
		await codeLoadSem.WaitAsync(ct).ConfigureAwait(false);
		try {
			return await Task.Run(() => loadCodeMod(stagedMod), ct).ConfigureAwait(false);
		} finally {
			codeLoadSem.Release();
		}
	}

	private LoadedCodeMod<TGameApi> loadCodeMod(StagedMod stagedMod) {
		var manifest = (CodeModManifest)stagedMod.Manifest;
		if (stagedMod.MainAssemblyPath is null || !File.Exists(stagedMod.MainAssemblyPath))
			throw new ModLoadException(manifest.OwnerID, $"entry assembly '{manifest.EntryAssembly}' not found");

		ModAlc alc = new(stagedMod.MainAssemblyPath, sharedAssemblies, $"mod:{stagedMod.Generation}");
		UntypedBoundedScope? scope = null;
		try {
			Assembly assembly = alc.LoadFromAssemblyPath(stagedMod.MainAssemblyPath);
			validateModAssemblyAttribute(manifest, assembly);
			foreach (Assembly asm in alc.Assemblies)
				nativeLibs.RegisterAssembly(asm, stagedMod.Generation);

			Type lifetimeIdentityType = ModTypeDiscovery.FindLifetimeIdentityType(assembly, stagedMod.MainAssemblyPath);
			scope = new UntypedBoundedScope(stagedMod.Generation, maxScopeTeardownParallelism);
			Type entryType = ModTypeDiscovery.FindEntrypointType(assembly, typeof(TGameApi), lifetimeIdentityType, stagedMod.MainAssemblyPath);
			object entrypoint = Activator.CreateInstance(entryType)!;
			object? reloadEntrypoint = null;
			if (manifest.LiveReloadable) {
				Type reloadEntrypointType = ModTypeDiscovery.FindReloadEntrypointType(assembly, typeof(TGameApi), lifetimeIdentityType, stagedMod.MainAssemblyPath);
				reloadEntrypoint = Activator.CreateInstance(reloadEntrypointType)!;
			}

			return new LoadedCodeMod<TGameApi> {
				Staged = stagedMod,
				AssemblyLoadContext = alc,
				Assembly = assembly,
				LifetimeIdentityType = lifetimeIdentityType,
				Scope = scope,
				Entrypoint = entrypoint,
				ReloadEntrypoint = reloadEntrypoint,
				LoadHooks = new GenerationPatchSet(),
			};
		} catch (Exception ex) when (!ExceptionPolicy.IsInternalState(ex)) {
			try {
				if (scope is not null)
					block(scope.InvalidateAsync(ReloadTeardownReason.FailureRollback, CancellationToken.None));
				foreach (Assembly asm in alc.Assemblies)
					AssemblyScrubber.ScrubStaticReferenceFields(asm);
				alc.Unload();
			} catch {
				// preserve original load failure
			}
			throw;
		}
	}

	private async ValueTask runLoadAsync(LoadedCodeMod<TGameApi> mod, CancellationToken ct) {
		object ctx = createLoadContext(mod, createApi(mod), diagnosticsSinkRegistry, hookTargetResolver);
		try {
			await invokeEntrypointAsync(mod, nameof(IModEntrypoint<,>.LoadAsync), ctx, ct).ConfigureAwait(false);
		} finally {
			(ctx as IStrongRefDroppable)?.DropStrongReferences();
			ctx = null!;
		}
	}

	private async ValueTask runLinkAsync(
		LoadedCodeMod<TGameApi> mod,
		IReadOnlyDictionary<string, LoadedDependencyInfo> owners,
		IReadOnlyDictionary<string, LoadedCodeDependencyInfo> codeOwners,
		CancellationToken ct
	) {
		Dictionary<string, LoadedDependencyInfo>? dependencies = buildDeclaredDependencyInfo(mod.Staged.Manifest, owners);
		Dictionary<string, LoadedCodeDependencyInfo>? codeDependencies = buildDeclaredDependencyInfo(mod.Staged.Manifest, codeOwners);
		object ctx = createLinkContext(mod, createApi(mod), diagnosticsSinkRegistry, dependencies, codeDependencies);
		try {
			await invokeEntrypointAsync(mod, nameof(IModEntrypoint<,>.LinkAsync), ctx, ct).ConfigureAwait(false);
		} finally {
			(ctx as IStrongRefDroppable)?.DropStrongReferences();
			ctx = null!;
			dependencies.Clear();
			dependencies = null;
			codeDependencies.Clear();
			codeDependencies = null;
		}
	}

	private async ValueTask activateOneAsync(LoadedCodeMod<TGameApi> mod, GameServices gameServices, CancellationToken ct) {
		if (mod.ActivationScope is not null)
			throw new InternalStateException("wasn't expecting this LoadedCodeMod to already have an ActivationScope");
		UntypedBoundedScope activationScope = new(mod.Staged.Generation, maxScopeTeardownParallelism);
		mod.ActivationScope = activationScope;
		object ctx = createActivateContext(mod, createApi(mod), diagnosticsSinkRegistry, gameServices);
		try {
			await invokeEntrypointAsync(mod, nameof(IModEntrypoint<,>.ActivateAsync), ctx, ct).ConfigureAwait(false);
			mod.Active = true;
		} catch {
			mod.Active = false;
			mod.ActivationScope = null;
			await activationScope.InvalidateAsync(ReloadTeardownReason.Deactivate, CancellationToken.None).ConfigureAwait(false);
			throw;
		} finally {
			(ctx as IStrongRefDroppable)?.DropStrongReferences();
			ctx = null!;
		}
	}

	private static async ValueTask deactivateOneAsync(LoadedCodeMod<TGameApi> mod, CancellationToken ct) {
		UntypedBoundedScope? activationScope = mod.ActivationScope;
		try {
			if (mod.Active)
				await invokeEntrypointAsync(mod, nameof(IModEntrypoint<,>.DeactivateAsync), null, ct).ConfigureAwait(false);
		} finally {
			mod.Active = false;
			mod.ActivationScope = null;
			if (activationScope is not null)
				await activationScope.InvalidateAsync(ReloadTeardownReason.Deactivate, CancellationToken.None).ConfigureAwait(false);
			activationScope = null;
		}
	}

	private async ValueTask deactivateSetAsync(HashSet<string> set, ResolvedModGraph graph, bool reverse, CancellationToken ct) {
		IEnumerable<IReadOnlyList<string>> waves = reverse ? graph.Waves.Waves.Reverse() : graph.Waves.Waves;
		foreach (IReadOnlyList<string> wave in waves) {
			List<Task> tasks = new();
			foreach (string id in wave)
				if (set.Contains(id) && activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod) && mod.Active)
					tasks.Add(deactivateOneAsync(mod, ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
	}

	private async ValueTask activateSetAsync(
		HashSet<string> set,
		ResolvedModGraph graph,
		Dictionary<string, LoadedCodeMod<TGameApi>> source,
		GameServices gameServices,
		CancellationToken ct
	) {
		foreach (IReadOnlyList<string> wave in graph.Waves.Waves) {
			List<Task> tasks = new();
			foreach (string id in wave)
				if (set.Contains(id) && source.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod) && !mod.Active)
					tasks.Add(activateOneAsync(mod, gameServices, ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
	}

	private async ValueTask disposeOldOwnerScopesAsync(IReadOnlySet<string> set, BoundaryPlan plan, CancellationToken ct) {
		foreach (string id in set) {
			ReloadTeardownReason reason = plan.DisableSet.Contains(id) ? ReloadTeardownReason.Disable : ReloadTeardownReason.Reload;
			if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
				await invalidateScopesAsync(mod, reason, ct).ConfigureAwait(false);
			if (activeContent.TryGetValue(id, out LoadedContentMod? content))
				await content.Scope.InvalidateAsync(reason, ct).ConfigureAwait(false);
		}
	}

	private void publishTransaction(Transaction transaction) {
		BoundaryPlan plan = transaction.Plan;
		discovered = transaction.CandidateDiscovered;
		activeGraph = transaction.CandidateGraph;
		foreach (string id in plan.DisableSet) {
			activeCode.Remove(id);
			activeContent.Remove(id);
			enabledOwners.Remove(id);
		}
		foreach (string id in plan.EnableSet)
			enabledOwners.Add(id);
		foreach (KeyValuePair<string, LoadedCodeMod<TGameApi>> kvp in transaction.PreparedCode)
			activeCode[kvp.Key] = kvp.Value;
		foreach (KeyValuePair<string, LoadedContentMod> kvp in transaction.PreparedContent)
			activeContent[kvp.Key] = kvp.Value;
		staged = staged.Where(mod => !plan.OldTouchedSet.Contains(mod.Manifest.OwnerID)).Concat(transaction.ReplacementStaged).ToArray();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async ValueTask unloadCodeGenerationAsync(LoadedCodeMod<TGameApi> mod, CancellationToken ct) {
		await invokeEntrypointAsync(mod, nameof(IModEntrypoint<,>.UnloadAsync), null, ct).ConfigureAwait(false);
	}

	private async ValueTask unloadAllCodeGenerationsAsync(CancellationToken ct) {
		foreach (IReadOnlyList<string> wave in activeGraph.Waves.Waves.Reverse()) {
			List<Task> tasks = new();
			foreach (string id in wave)
				if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
					tasks.Add(unloadCodeGenerationAsync(mod, ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async ValueTask startDestroyPreparedCodeGenerationAsync(LoadedCodeMod<TGameApi> mod, ReloadTeardownReason reason, CancellationToken ct) {
		try {
			await unloadCodeGenerationAsync(mod, ct).ConfigureAwait(false);
		} finally {
			await invalidateScopesAsync(mod, reason, ct).ConfigureAwait(false);
		}
	}

	private static BoundaryPlanReadiness validateReloadCapabilitiesAtBoundary(
		ResolvedModGraph graph,
		IReadOnlySet<string> reloadSet,
		ReloadBoundaryKind boundary,
		bool hasExplicitLiveRequest,
		bool oldManifest
	) {
		foreach (string id in reloadSet) {
			ModManifest manifest = graph.Mods[id].Manifest;
			if (manifest.Reloadability == ModReloadability.None)
				throw new ModLoadException(id, $"{(oldManifest ? "mod" : "new mod")} '{id}' is not reloadable");

			if (boundary == ReloadBoundaryKind.Live && manifest.Reloadability != ModReloadability.Live) {
				if (hasExplicitLiveRequest)
					throw new ModLoadException(id, $"{(oldManifest ? "mod" : "new mod")} '{id}' is not live-reloadable");
				return BoundaryPlanReadiness.WaitForStrongerBoundary;
			}
		}
		return BoundaryPlanReadiness.Ready;
	}

	private static bool isEligibleAtBoundary(in PendingOp op, ReloadBoundaryKind boundary) => op.Kind switch {
		OpKind.Reload => op.ReloadKind is ReloadRequestKind request && requestCanRunAtBoundary(request, boundary),
		OpKind.Disable => boundary == ReloadBoundaryKind.Safe,
		OpKind.Enable => boundary == ReloadBoundaryKind.Safe,
		_ => false,
	};

	private static bool requestCanRunAtBoundary(ReloadRequestKind request, ReloadBoundaryKind boundary) => request.Tag switch {
		ReloadRequestKind.Case.Any => true,
		ReloadRequestKind.Case.SafeBoundary => boundary == ReloadBoundaryKind.Safe,
		ReloadRequestKind.Case.Live => boundary is ReloadBoundaryKind.Live or ReloadBoundaryKind.Safe,
		_ => throw new UnreachableException(),
	};

	private static void validateModAssemblyAttribute(CodeModManifest manifest, Assembly assembly) {
		ModAssemblyAttribute attribute = assembly.GetCustomAttribute<ModAssemblyAttribute>() ??
			throw new ModLoadException(manifest.OwnerID, "entry assembly is missing ModAssembly attribute");
		if (!ModMetadataValidation.ValidateOwnerID(attribute.OwnerID, out string? err))
			throw new ModLoadException(manifest.OwnerID, $"assembly attribute OwnerID '{attribute.OwnerID}' is invalid: {err}");
		if (attribute.OwnerID != manifest.OwnerID)
			throw new ModLoadException(manifest.OwnerID, $"manifest id '{manifest.OwnerID}' does not match assembly attribute OwnerID '{attribute.OwnerID}'");
		if (attribute.HotReloadLevel != (ModAssemblyHotReloadLevel)manifest.Reloadability)
			throw new ModLoadException(
				manifest.OwnerID,
				$"manifest reloadability '{manifest.Reloadability}' does not match assembly attribute HotReloadLevel '{attribute.HotReloadLevel}'"
			);
	}

	private static readonly MethodInfo createLinkedCancellationSourceMethod = getCreateLinkedCancellationSourceMethod();

	private static MethodInfo getCreateLinkedCancellationSourceMethod() {
		foreach (MethodInfo mi in typeof(UntypedBoundedScope).GetMethods(BindingFlags.Instance | BindingFlags.Public)) {
			ParameterInfo[] parameters = mi.GetParameters();
			if (
				mi.Name == nameof(UntypedBoundedScope.CreateLinkedCts) &&
				mi.IsGenericMethodDefinition &&
				parameters.Length == 1 &&
				parameters[0].ParameterType == typeof(CancellationToken)
			)
				return mi;
		}
		throw new MissingMethodException(typeof(UntypedBoundedScope).FullName, nameof(UntypedBoundedScope.CreateLinkedCts));
	}

	private static IDisposable createLinkedGenerationCancellationSource(LoadedCodeMod<TGameApi> mod, CancellationToken ct, out object token) {
		object gcs = createLinkedCancellationSourceMethod.MakeGenericMethod(mod.LifetimeIdentityType).Invoke(mod.Scope, new object[] { ct })!;
		PropertyInfo tokenProperty = gcs.GetType().GetProperty(nameof(BoundedCts<>.Token)) ??
			throw new MissingMemberException(gcs.GetType().FullName, nameof(BoundedCts<>.Token));
		token = tokenProperty.GetValue(gcs)!;
		return (IDisposable)gcs;
	}

	private static async ValueTask invokeEntrypointAsync(LoadedCodeMod<TGameApi> mod, string methodName, object? ctx, CancellationToken ct) {
		Type entrypointInterface = typeof(IModEntrypoint<,>).MakeGenericType(typeof(TGameApi), mod.LifetimeIdentityType);
		MethodInfo mi = entrypointInterface.GetMethod(methodName) ?? throw new MissingMethodException(entrypointInterface.FullName, methodName);
		using IDisposable gcs = createLinkedGenerationCancellationSource(mod, ct, out object gct);
		object?[] args = ctx is null ? new object?[] { gct } : new object?[] { ctx, gct };
		object? result = invokeModMethod(mi, mod.Entrypoint, args);
		await expectValueTask(result, methodName).ConfigureAwait(false);
	}

	private static async ValueTask<ModLiveStateBlob> invokeSaveStateAsync(LoadedCodeMod<TGameApi> mod, object ctx, CancellationToken ct) {
		if (mod.ReloadEntrypoint is null)
			throw new InternalStateException("this mod has no ReloadEntrypoint");
		Type reloadInterface = typeof(IModReloadEntrypoint<,>).MakeGenericType(typeof(TGameApi), mod.LifetimeIdentityType);
		MethodInfo mi = reloadInterface.GetMethod(nameof(IModReloadEntrypoint<,>.SaveStateAsync)) ??
			throw new MissingMethodException(reloadInterface.FullName, nameof(IModReloadEntrypoint<,>.SaveStateAsync));
		using IDisposable gcs = createLinkedGenerationCancellationSource(mod, ct, out object gct);
		object? result = invokeModMethod(mi, mod.ReloadEntrypoint, new object?[] { ctx, gct });
		if (result is ValueTask<ModLiveStateBlob> vt)
			return await vt.ConfigureAwait(false);
		throw new InternalStateException(
			$"reload entrypoint method '{mi.Name}' returned unexpected type '{result?.GetType().FullName ?? "<null>"}', did some other unrelated object somehow get assigned to mod.ReloadEntrypoint?"
		);
	}

	private static async ValueTask invokeRestoreStateAsync(LoadedCodeMod<TGameApi> mod, object ctx, ModLiveStateBlob state, CancellationToken ct) {
		object reloadEntrypoint = mod.ReloadEntrypoint ?? throw new InvalidOperationException($"mod '{mod.Staged.Manifest.OwnerID}' has no reload entrypoint instance");
		Type reloadInterface = typeof(IModReloadEntrypoint<,>).MakeGenericType(typeof(TGameApi), mod.LifetimeIdentityType);
		MethodInfo mi = reloadInterface.GetMethod(nameof(IModReloadEntrypoint<,>.RestoreStateAsync)) ??
			throw new MissingMethodException(reloadInterface.FullName, nameof(IModReloadEntrypoint<,>.RestoreStateAsync));
		using IDisposable gcs = createLinkedGenerationCancellationSource(mod, ct, out object gct);
		object? result = invokeModMethod(mi, reloadEntrypoint, new object?[] { ctx, state, gct });
		await expectValueTask(result, mi.Name).ConfigureAwait(false);
	}

	private static async ValueTask invalidateScopesAsync(LoadedCodeMod<TGameApi> mod, ReloadTeardownReason reason, CancellationToken ct) {
		if (mod.ActivationScope is not null) {
			await mod.ActivationScope.InvalidateAsync(reason, ct);
			mod.ActivationScope = null;
		}
		await mod.Scope.InvalidateAsync(reason, ct);
	}

	private static object? invokeModMethod(MethodInfo mi, object target, object?[] args) {
		try {
			return mi.Invoke(target, args);
		} catch (TargetInvocationException ex) when (ex.InnerException is not null) {
			ExceptionPolicy.ThrowIfInternalState(ex);
			throw ExceptionSnapshot.FromException(ex).ToException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ValueTask expectValueTask(object? result, string methodName) {
		if (result is not ValueTask vt)
			throw new InternalStateException(
				$"method '{methodName}' returned unexpected type '{result?.GetType().FullName ?? "<null>"}', did some other unrelated object somehow get assigned to mod.Entrypoint or another internal object?"
			);
		return vt;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private TGameApi createApi(LoadedCodeMod<TGameApi> mod) => apiFactory(new ModApiFactoryContext(mod.Staged.Manifest.OwnerID, mod.Scope));

	private static object createLoadHookDeclarations(LoadedCodeMod<TGameApi> mod, HookTargetResolver resolver) => Activator.CreateInstance(
		typeof(ModHookDeclarations<,>).MakeGenericType(typeof(TGameApi), mod.LifetimeIdentityType),
		mod,
		resolver,
		HookDeclarationPhase.Load,
		mod.Staged.Generation
	)!;

	private static object createLoadContext(
		LoadedCodeMod<TGameApi> mod,
		TGameApi api,
		DiagnosticsSinkRegistry diagnosticsSinkRegistry,
		HookTargetResolver resolver
	) => Activator.CreateInstance(
		typeof(ModLoadContextImpl<,>).MakeGenericType(typeof(TGameApi), mod.LifetimeIdentityType),
		createLoadHookDeclarations(mod, resolver),
		mod.Staged.Manifest.OwnerID,
		mod.Staged.Manifest.Version,
		api,
		new OwnerDiagnostics(mod.Staged.Manifest.OwnerID, diagnosticsSinkRegistry, mod.Staged.Generation),
		mod.Scope,
		diagnosticsSinkRegistry
	)!;

	private static object createLinkContext(
		LoadedCodeMod<TGameApi> mod,
		TGameApi api,
		DiagnosticsSinkRegistry diagnosticsSinkRegistry,
		IReadOnlyDictionary<string, LoadedDependencyInfo> dependencies,
		IReadOnlyDictionary<string, LoadedCodeDependencyInfo> codeDependencies
	) => Activator.CreateInstance(
		typeof(ModLinkContextImpl<,>).MakeGenericType(typeof(TGameApi), mod.LifetimeIdentityType),
		dependencies,
		codeDependencies,
		mod.Staged.Manifest.OwnerID,
		mod.Staged.Manifest.Version,
		api,
		new OwnerDiagnostics(mod.Staged.Manifest.OwnerID, diagnosticsSinkRegistry, mod.Staged.Generation),
		mod.Scope,
		diagnosticsSinkRegistry
	)!;

	private static object createActivateContext(
		LoadedCodeMod<TGameApi> mod,
		TGameApi api,
		DiagnosticsSinkRegistry diagnosticsSinkRegistry,
		GameServices gameServices
	) => Activator.CreateInstance(
		typeof(ModActivateContextImpl<,>).MakeGenericType(typeof(TGameApi), mod.LifetimeIdentityType),
		gameServices,
		mod.ActivationScope ?? throw new InternalStateException("expected this LoadedCodeMod to have an ActivationScope"),
		mod.Staged.Manifest.OwnerID,
		mod.Staged.Manifest.Version,
		api,
		new OwnerDiagnostics(mod.Staged.Manifest.OwnerID, diagnosticsSinkRegistry, mod.Staged.Generation),
		mod.Scope,
		diagnosticsSinkRegistry
	)!;

	private static object createReloadContext(
		LoadedCodeMod<TGameApi> mod,
		TGameApi api,
		DiagnosticsSinkRegistry diagnosticsSinkRegistry,
		IReadOnlySet<string> reloadSet,
		GameServices? gameServices
	) => Activator.CreateInstance(
		typeof(ModReloadContextImpl<,>).MakeGenericType(typeof(TGameApi), mod.LifetimeIdentityType),
		gameServices,
		reloadSet,
		mod.Staged.Manifest.OwnerID,
		mod.Staged.Manifest.Version,
		api,
		new OwnerDiagnostics(mod.Staged.Manifest.OwnerID, diagnosticsSinkRegistry, mod.Staged.Generation),
		mod.Scope,
		diagnosticsSinkRegistry
	)!;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Dictionary<string, LoadedDependencyInfo> buildOwnerInfo(IEnumerable<StagedMod> stagedMods) =>
		stagedMods.ToDictionary(
			static mod => mod.Manifest.OwnerID,
			static mod => new LoadedDependencyInfo(
				mod.Manifest.OwnerID,
				mod.Manifest.Version,
				mod.Generation
			),
			StringComparer.Ordinal
		);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Dictionary<string, LoadedCodeDependencyInfo> buildCodeOwnerInfo(IEnumerable<LoadedCodeMod<TGameApi>> mods) =>
		mods.ToDictionary(
			static mod => mod.Staged.Manifest.OwnerID,
			static mod => new LoadedCodeDependencyInfo(
				mod.Staged.Manifest.OwnerID,
				mod.Staged.Manifest.Version,
				mod.Staged.Generation,
				mod.Assembly
			),
			StringComparer.Ordinal
		);

	private static Dictionary<string, TInfo> buildDeclaredDependencyInfo<TInfo>(ModManifest manifest, IReadOnlyDictionary<string, TInfo> loaded) where TInfo : struct {
		Dictionary<string, TInfo> result = new(StringComparer.Ordinal);
		foreach (ModRelationshipManifest relationship in manifest.Relationships)
			if (loaded.TryGetValue(relationship.OwnerID, out TInfo info))
				result[relationship.OwnerID] = info;
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void requirePhase(RuntimePhase expected, string operation) {
		if (phase == RuntimePhase.Faulted)
			throw new InvalidOperationException("runtime has internally faulted and isn't usable anymore; sorry");
		if (phase != expected)
			throw new InvalidOperationException($"{operation} requires phase {expected}, but current phase is {phase}");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private PendingAlcUnload detachForUnload(LoadedCodeMod<TGameApi> mod) {
		nativeLibs.UnregisterGeneration(mod.Staged.Generation);
		ReloadGeneration generation = mod.Staged.Generation;
		ModAlc alc = mod.AssemblyLoadContext;
		mod.DropStrongReferences();
		return new PendingAlcUnload(generation, alc);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference beginUnload(PendingAlcUnload pending) {
		ModAlc alc = pending.Alc;
		foreach (Assembly asm in alc.Assemblies)
			AssemblyScrubber.ScrubStaticReferenceFields(asm);
		pending.DropStrongReferences();
		WeakReference weak = new(alc, trackResurrection: false);
		alc.Unload();
		return weak;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool probeUnload(WeakReference weak, int maxAttempts) {
		for (int i = 0; i < maxAttempts && weak.IsAlive; i++) {
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}
		return !weak.IsAlive;
	}

	private static void unload(PendingAlcUnload pending, OwnerDiagnostics diagnostics) {
		WeakReference weak = beginUnload(pending);
		if (!probeUnload(weak, maxAttempts: MaxAlcUnloadGcAttempts))
			diagnostics.Warning($"{pending.Generation} is still alive after {MaxAlcUnloadGcAttempts} unload attempts, giving up");
	}

	private static async ValueTask copyDirectoryAsync(string source, string target, CancellationToken ct) {
		foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));

		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			ct.ThrowIfCancellationRequested();
			string rel = Path.GetRelativePath(source, file);
			string dst = Path.Combine(target, rel);
			Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
			await using FileStream @in = File.OpenRead(file);
			await using FileStream @out = File.Create(dst);
			await @in.CopyToAsync(@out, ct).ConfigureAwait(false);
		}
	}
}
