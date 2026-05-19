// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

using Injure.ModKit.Abstractions;
using Injure.ModKit.Loader;
using Injure.ModKit.MonoMod;

namespace Injure.ModKit.Runtime;

public readonly record struct ModApiFactoryContext(
	string OwnerID,
	OwnerScope OwnerScope
);

public readonly record struct ModRuntimeOptions<TGameApi> {
	public required string ModDirectory { get; init; }
	public required string CacheDirectory { get; init; }
	public required Func<ModApiFactoryContext, TGameApi> ApiFactory { get; init; }
	public required IReadOnlyList<string> SharedAssemblies { get; init; }
	public required int MaxParallelCodeLoads { get; init; }
}

internal sealed class ModLoadLinkContextImpl<TGameApi>(IReadOnlyDictionary<string, LoadedOwnerInfo> loaded) : IModLoadContext<TGameApi>, IModLinkContext<TGameApi> {
	private readonly IReadOnlyDictionary<string, LoadedOwnerInfo> loaded = loaded;

	public required string OwnerID { get; init; }
	public required Semver Version { get; init; }
	public required TGameApi Api { get; init; }
	public required OwnerScope OwnerScope { get; init; }
	public required ReloadGenerationScope GenerationScope { get; init; }
	public required CancellationToken UnloadToken { get; init; }

	public bool TryGetLoadedDependency(string id, out LoadedOwnerInfo info) => loaded.TryGetValue(id, out info);
}

internal sealed class ModReloadContextImpl<TGameApi> : IModReloadContext<TGameApi> {
	public required TGameApi Api { get; init; }
	public required ReloadGeneration Generation { get; init; }
	public required IReadOnlySet<string> ReloadSet { get; init; }
}

public sealed class ModRuntime<TGameApi>(ModRuntimeOptions<TGameApi> options) {
	private readonly record struct ActiveDependent(string OwnerID, bool IsHard);

	private sealed class BoundaryPlan {
		public required ReloadRequestKind ReloadKind { get; init; }
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
		public required IReadOnlyList<DiscoveredMod> CandidateDiscovered { get; init; }
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

	public const string ManifestJson = "manifest.json";
	public const int MaxAlcUnloadGcAttempts = 8;

	private readonly int maxParallelDomains = options.MaxParallelCodeLoads;
	private readonly string modDir = options.ModDirectory;
	private readonly string cacheDir = options.CacheDirectory;
	private readonly Func<ModApiFactoryContext, TGameApi> apiFactory = options.ApiFactory;
	private readonly IReadOnlyList<string> sharedAssemblies = options.SharedAssemblies;
	private readonly SemaphoreSlim codeLoadSem = new(options.MaxParallelCodeLoads, options.MaxParallelCodeLoads);
	private readonly SemaphoreSlim writeLock = new(1, 1);

	private RuntimePhase phase = RuntimePhase.Empty;
	private IReadOnlyList<DiscoveredMod> discovered = Array.Empty<DiscoveredMod>();
	private ResolvedModGraph activeGraph;
	private IReadOnlyList<StagedMod> staged = Array.Empty<StagedMod>();
	private readonly Dictionary<string, LoadedCodeMod<TGameApi>> activeCode = new(StringComparer.Ordinal);
	private readonly Dictionary<string, LoadedContentMod> activeContent = new(StringComparer.Ordinal);
	private readonly HashSet<string> enabledOwners = new(StringComparer.Ordinal);
	private readonly HookTargetResolver hookTargetResolver = new(AssemblyLoadContext.Default.Assemblies);
	private ulong nextGeneration = 0;

	private readonly Lock opLock = new();
	private readonly List<PendingOp> pendingOps = new();
	private ulong nextOpSeq = 0;

	public async ValueTask StartAsync(CancellationToken ct) {
		await DiscoverAsync(ct).ConfigureAwait(false);
		await ResolveAsync(ct).ConfigureAwait(false);
		await StageAsync(ct).ConfigureAwait(false);
		await LoadCodeAsync(ct).ConfigureAwait(false);
		await DiscoverHooksAsync(ct).ConfigureAwait(false);
		await LoadAsync(ct).ConfigureAwait(false);
		await ApplyLoadHooksAsync(ct).ConfigureAwait(false);
		await LinkAsync(ct).ConfigureAwait(false);
		await ActivateAsync(ct).ConfigureAwait(false);
	}

	public async ValueTask DiscoverAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Empty, nameof(DiscoverAsync));
		List<DiscoveredMod> result = new();
		try {
			Directory.CreateDirectory(modDir);
		} catch {
			// swallow
		}
		foreach (string manifestPath in enumerateManifests(modDir)) {
			ct.ThrowIfCancellationRequested();
			ModManifest manifest = await ManifestReader.ReadAsync(manifestPath, ct).ConfigureAwait(false);
			string root = Path.GetDirectoryName(manifestPath)!;
			result.Add(new DiscoveredMod(new ModSource(root, manifestPath), manifest));
		}
		discovered = result;
		enabledOwners.Clear();
		foreach (DiscoveredMod mod in discovered)
			enabledOwners.Add(mod.Manifest.OwnerID);
		phase = RuntimePhase.Discovered;
	}

	public ValueTask ResolveAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Discovered, nameof(ResolveAsync));
		ct.ThrowIfCancellationRequested();
		activeGraph = ModRelationshipResolver.Resolve(discovered.Where(mod => enabledOwners.Contains(mod.Manifest.OwnerID)).ToArray());
		phase = RuntimePhase.Resolved;
		return ValueTask.CompletedTask;
	}

	public async ValueTask StageAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Resolved, nameof(StageAsync));
		ResolvedModGraph graph = activeGraph;
		List<StagedMod> result = new();
		foreach (ResolvedMod mod in graph.ModsInDeterministicOrder) {
			ct.ThrowIfCancellationRequested();
			result.Add(await stageOneAsync(mod.Source, mod.Manifest, ct).ConfigureAwait(false));
		}
		staged = result;
		phase = RuntimePhase.Staged;
	}

	public async ValueTask LoadCodeAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Staged, nameof(LoadCodeAsync));
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
	}

	public async ValueTask DiscoverHooksAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.CodeLoaded, nameof(DiscoverHooksAsync));
		ct.ThrowIfCancellationRequested();
		foreach (LoadedCodeMod<TGameApi> mod in activeCode.Values)
			HookDiscoverer<TGameApi>.DiscoverLoadHooks(mod, hookTargetResolver);
		phase = RuntimePhase.HooksDiscovered;
	}

	public async ValueTask LoadAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.HooksDiscovered, nameof(LoadAsync));
		ct.ThrowIfCancellationRequested();
		Dictionary<string, LoadedOwnerInfo> owners = buildOwnerInfo(staged);
		await Task.WhenAll(activeCode.Values.Select(mod => runLoadAsync(mod, owners, ct).AsTask())).ConfigureAwait(false);
		phase = RuntimePhase.Loaded;
	}

	public async ValueTask ApplyLoadHooksAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Loaded, nameof(ApplyLoadHooksAsync));
		ct.ThrowIfCancellationRequested();
		await HookApplier<TGameApi>.ApplyLoadHooksAsync(activeCode.Values.ToArray(), maxParallelDomains, ct).ConfigureAwait(false);
		phase = RuntimePhase.LoadHooksApplied;
	}	

	public async ValueTask LinkAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.LoadHooksApplied, nameof(LinkAsync));
		ResolvedModGraph graph = activeGraph;
		Dictionary<string, LoadedOwnerInfo> owners = buildOwnerInfo(staged);
		foreach (IReadOnlyList<string> wave in graph.Waves.Waves) {
			ct.ThrowIfCancellationRequested();
			List<Task> tasks = new();
			foreach (string id in wave)
				if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
					tasks.Add(runLinkAsync(mod, owners, ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
		phase = RuntimePhase.Linked;
	}

	public async ValueTask ActivateAsync(CancellationToken ct) {
		requirePhase(RuntimePhase.Linked, nameof(ActivateAsync));
		ct.ThrowIfCancellationRequested();
		ResolvedModGraph graph = activeGraph;
		await activateSetAsync(activeCode.Keys.ToHashSet(), graph, activeCode, ct).ConfigureAwait(false);
		phase = RuntimePhase.Active;
	}

	public void RequestReload(string ownerID, ReloadRequestKind kind) {
		requirePhase(RuntimePhase.Active, nameof(RequestReload));
		lock (opLock)
			pendingOps.Add(new PendingOp(Seq: ++nextOpSeq, Kind: OpKind.Reload, OwnerID: ownerID, ReloadKind: kind));
	}

	public void RequestDisable(string ownerID, DisableRequestKind kind) {
		requirePhase(RuntimePhase.Active, nameof(RequestDisable));
		lock (opLock)
			pendingOps.Add(new PendingOp(Seq: ++nextOpSeq, Kind: OpKind.Disable, OwnerID: ownerID, DisableKind: kind));
	}

	public void RequestEnable(string ownerID, EnableRequestKind kind) {
		requirePhase(RuntimePhase.Active, nameof(RequestEnable));
		lock (opLock)
			pendingOps.Add(new PendingOp(Seq: ++nextOpSeq, Kind: OpKind.Enable, OwnerID: ownerID, EnableKind: kind));
	}

	public void AtSafeBoundary(CancellationToken ct = default) => block(processBoundaryAsync(ReloadBoundaryKind.Safe, ct));
	public void AtLiveBoundary(CancellationToken ct = default) => block(processBoundaryAsync(ReloadBoundaryKind.Live, ct));
	public ValueTask AtSafeBoundaryAsync(CancellationToken ct = default) => processBoundaryAsync(ReloadBoundaryKind.Safe, ct);
	public ValueTask AtLiveBoundaryAsync(CancellationToken ct = default) => processBoundaryAsync(ReloadBoundaryKind.Live, ct);

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

	private PendingOp[] takePendingBatch(ReloadBoundaryKind boundary) {
		lock (opLock) {
			List<PendingOp> batch = new();
			for (int i = pendingOps.Count - 1; i >= 0; i--) {
				PendingOp op = pendingOps[i];
				if (!isEligibleAtBoundary(op, boundary))
					continue;
				batch.Add(op);
				pendingOps.RemoveAt(i);
			}
			batch.Sort(static (a, b) => a.Seq.CompareTo(b.Seq));
			return batch.Count == 0 ? Array.Empty<PendingOp>() : batch.ToArray();
		}
	}

	private async ValueTask processBoundaryAsync(ReloadBoundaryKind boundary, CancellationToken ct) {
		await writeLock.WaitAsync(ct).ConfigureAwait(false);
		try {
			for (;;) {
				PendingOp[] batch = takePendingBatch(boundary);
				if (batch.Length == 0)
					return;

				BoundaryPlan plan = createBoundaryPlan(batch);
				if (plan.IsNoOp)
					continue;

				Transaction transaction = await prepareTransactionAsync(plan, ct).ConfigureAwait(false);
				ModOperationResult r = await commitTransactionAsync(transaction, ct).ConfigureAwait(false);
				transaction.DropContainersOnly();
#pragma warning disable IDE0059 // unnecessary assignment to local
				transaction = null!;
#pragma warning restore IDE0059 // unnecessary assignment to local
				foreach (PendingAlcUnload pending in r.PendingUnloads) {
					WeakReference weak = beginUnload(pending);
					if (!probeUnload(weak, maxAttempts: MaxAlcUnloadGcAttempts))
						Console.Error.WriteLine($"warning: ALC for {pending.OwnerID} is still alive after unload request");
				}
			}
		} finally {
			writeLock.Release();
		}
	}

	private BoundaryPlan createBoundaryPlan(PendingOp[] batch) {
		Array.Sort(batch, static (a, b) => a.Seq.CompareTo(b.Seq));

		HashSet<string> candidateEnabled = new(enabledOwners, StringComparer.Ordinal);
		HashSet<string> reloadRoots = new(StringComparer.Ordinal);
		bool hasStructuralOps = false;
		ReloadRequestKind reloadKind = ReloadRequestKind.SafeBoundary;

		foreach (PendingOp op in batch) {
			switch (op.Kind) {
			case OpKind.Reload:
				if (!candidateEnabled.Contains(op.OwnerID))
					break;
				reloadRoots.Add(op.OwnerID);
				if (op.ReloadKind == ReloadRequestKind.Live)
					reloadKind = ReloadRequestKind.Live;
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
		validateReloadCapabilities(reloadSet, reloadKind);
		return new BoundaryPlan {
			ReloadKind = reloadKind,
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

		Dictionary<string, DiscoveredMod> discoveredMap = discovered.ToDictionary(static mod => mod.Manifest.OwnerID, StringComparer.Ordinal);

		if (!discoveredMap.ContainsKey(ownerID))
			throw new ModLoadException(ownerID, $"unknown mod '{ownerID}'");

		bool enableRequiredDependencies = kind.Tag is
			EnableRequestKind.Case.EnableRequiredDependencies or
			EnableRequestKind.Case.EnableRequiredDependenciesAndReloadOptionalDependents;
		HashSet<string> newlyEnabled = computeRequiredEnableClosure(ownerID, enableRequiredDependencies, discoveredMap);
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

	private async ValueTask<Transaction> prepareTransactionAsync(BoundaryPlan plan, CancellationToken ct) {
		HashSet<string> prepareSet = plan.PrepareSet;
		HashSet<string> oldTouchedSet = plan.OldTouchedSet;

		Dictionary<string, DiscoveredMod> candidateDiscovered = discovered.ToDictionary(static mod => mod.Manifest.OwnerID, StringComparer.Ordinal);
		foreach (string id in prepareSet) {
			if (!candidateDiscovered.TryGetValue(id, out DiscoveredMod old))
				throw new ModLoadException(id, $"unknown mod '{id}'");
			ModManifest manifest = await ManifestReader.ReadAsync(old.Source.ManifestPath, ct).ConfigureAwait(false);
			candidateDiscovered[id] = new DiscoveredMod(old.Source, manifest);
		}

		ResolvedModGraph candidateGraph = ModRelationshipResolver.Resolve(
			candidateDiscovered.Values.Where(mod => plan.CandidateEnabledOwners.Contains(mod.Manifest.OwnerID)).ToArray()
		);
		validateCandidateReloadCapabilities(candidateGraph, plan.ReloadSet, plan.ReloadKind);

		List<StagedMod> replacementStaged = new();
		foreach (ResolvedMod resolved in candidateGraph.ModsInDeterministicOrder) {
			if (!prepareSet.Contains(resolved.Manifest.OwnerID))
				continue;
			replacementStaged.Add(await stageOneAsync(resolved.Source, resolved.Manifest, ct).ConfigureAwait(false));
		}

		Dictionary<string, LoadedCodeMod<TGameApi>> oldCode = activeCode
			.Where(kvp => oldTouchedSet.Contains(kvp.Key))
			.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal);
		Dictionary<string, LoadedContentMod> oldContent = activeContent
			.Where(kvp => oldTouchedSet.Contains(kvp.Key))
			.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal);

		Dictionary<string, LoadedCodeMod<TGameApi>> preparedCode = new(StringComparer.Ordinal);
		Dictionary<string, LoadedContentMod> preparedContent = new(StringComparer.Ordinal);

		try {
			foreach (StagedMod stagedMod in replacementStaged) {
				if (stagedMod.Manifest is CodeModManifest) {
					LoadedCodeMod<TGameApi> loaded = await loadCodeModBoundedAsync(stagedMod, ct).ConfigureAwait(false);
					HookDiscoverer<TGameApi>.DiscoverLoadHooks(loaded, hookTargetResolver);
					preparedCode.Add(stagedMod.Manifest.OwnerID, loaded);
				} else if (stagedMod.Manifest is ContentModManifest) {
					preparedContent.Add(stagedMod.Manifest.OwnerID, createLoadedContentMod(stagedMod));
				}
			}

			Dictionary<string, LoadedOwnerInfo> candidateOwners = buildOwnerInfo(
				candidateGraph.ModsInDeterministicOrder.Select(static mod => mod.Manifest)
			);

			await Task.WhenAll(preparedCode.Values.Select(mod => runLoadAsync(mod, candidateOwners, ct).AsTask())).ConfigureAwait(false);
			foreach (IReadOnlyList<string> wave in candidateGraph.Waves.Waves) {
				List<Task> tasks = new();
				foreach (string id in wave)
					if (preparedCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
						tasks.Add(runLinkAsync(mod, candidateOwners, ct).AsTask());
				try {
					await Task.WhenAll(tasks).ConfigureAwait(false);
				} finally {
					tasks.Clear();
				}
			}
		} catch {
			foreach (LoadedCodeMod<TGameApi> mod in preparedCode.Values)
				await destroyPreparedCodeGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
			foreach (LoadedContentMod mod in preparedContent.Values)
				await destroyPreparedContentGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
			throw;
		}

		return new Transaction {
			Plan = plan,
			CandidateDiscovered = candidateDiscovered.Values.ToArray(),
			CandidateGraph = candidateGraph,
			ReplacementStaged = replacementStaged,
			OldCode = oldCode,
			OldContent = oldContent,
			PreparedCode = preparedCode,
			PreparedContent = preparedContent,
		};
	}

	private async ValueTask<ModOperationResult> commitTransactionAsync(Transaction transaction, CancellationToken ct) {
		BoundaryPlan plan = transaction.Plan;
		Dictionary<string, ModLiveStateBlob> capturedState = new(StringComparer.Ordinal);
		bool destructiveBoundaryCrossed = false;
		ExceptionSnapshot reloadErr;
		try {
			FrozenSet<string> reloadSetSnapshot = plan.ReloadSet.ToFrozenSet(StringComparer.Ordinal);

			if (!plan.IsStructural && plan.ReloadKind == ReloadRequestKind.Live) {
				foreach (string id in plan.ReloadSet) {
					if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? old) && old.ReloadEntrypoint is not null) {
						ModReloadContextImpl<TGameApi> ctx = new() {
							Api = createApi(old),
							Generation = old.Staged.Generation,
							ReloadSet = reloadSetSnapshot,
						};
						capturedState.Add(id, await old.ReloadEntrypoint.SaveStateAsync(ctx, ct).ConfigureAwait(false));
					}
				}
			}

			try {
				await deactivateSetAsync(plan.OldTouchedSet, activeGraph, reverse: true, ct).ConfigureAwait(false);
			} finally {
				destructiveBoundaryCrossed = true;
			}

			await disposeOldOwnerScopesAsync(plan.OldTouchedSet).ConfigureAwait(false);

			await HookApplier<TGameApi>.ApplyLoadHooksAsync(transaction.PreparedCode.Values.ToArray(), maxParallelDomains, ct).ConfigureAwait(false);

			if (!plan.IsStructural && plan.ReloadKind == ReloadRequestKind.Live) {
				foreach (KeyValuePair<string, ModLiveStateBlob> kvp in capturedState) {
					if (transaction.PreparedCode.TryGetValue(kvp.Key, out LoadedCodeMod<TGameApi>? next) && next.ReloadEntrypoint is not null) {
						ModReloadContextImpl<TGameApi> ctx = new() {
							Api = createApi(next),
							Generation = next.Staged.Generation,
							ReloadSet = reloadSetSnapshot,
						};
						await next.ReloadEntrypoint.RestoreStateAsync(ctx, kvp.Value, ct).ConfigureAwait(false);
					}
				}
			}

			await activateSetAsync(plan.PrepareSet, transaction.CandidateGraph, transaction.PreparedCode, ct).ConfigureAwait(false);
			publishTransaction(transaction);

			List<PendingAlcUnload> oldUnloads = new();
			foreach (LoadedCodeMod<TGameApi> mod in transaction.OldCode.Values) {
				ReloadInvalidationReason reason = plan.DisableSet.Contains(mod.Staged.Manifest.OwnerID)
					? ReloadInvalidationReason.Disable
					: ReloadInvalidationReason.Reload;
				await destroyPreparedCodeGenerationNoUnloadAsync(mod, reason, ct).ConfigureAwait(false);
				oldUnloads.Add(detachForUnload(mod));
			}

			foreach (LoadedContentMod mod in transaction.OldContent.Values) {
				ReloadInvalidationReason reason = plan.DisableSet.Contains(mod.Staged.Manifest.OwnerID)
					? ReloadInvalidationReason.Disable
					: ReloadInvalidationReason.Reload;
				await destroyPreparedContentGenerationAsync(mod, reason, ct).ConfigureAwait(false);
			}

			return ModOperationResult.Succeeded(
				plan.ReloadSet.ToFrozenSet(StringComparer.Ordinal),
				plan.EnableSet.ToFrozenSet(StringComparer.Ordinal),
				plan.DisableSet.ToFrozenSet(StringComparer.Ordinal),
				plan.OldTouchedSet.ToFrozenSet(StringComparer.Ordinal),
				oldUnloads
			);
		} catch (Exception ex) {
			reloadErr = ExceptionSnapshot.FromException(ex);
			ex = null!;
		}

		if (!destructiveBoundaryCrossed) {
			foreach (LoadedCodeMod<TGameApi> mod in transaction.PreparedCode.Values)
				await destroyPreparedCodeGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
			foreach (LoadedContentMod mod in transaction.PreparedContent.Values)
				await destroyPreparedContentGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
			throw reloadErr.ToException();
		}

		List<PendingAlcUnload> preparedUnloads = new();
		List<ExceptionSnapshot> rollbackErrs = new();
		try {
			foreach (LoadedCodeMod<TGameApi> mod in transaction.PreparedCode.Values) {
				await destroyPreparedCodeGenerationNoUnloadAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
				preparedUnloads.Add(detachForUnload(mod));
			}
			foreach (LoadedContentMod mod in transaction.PreparedContent.Values)
				await destroyPreparedContentGenerationAsync(mod, ReloadInvalidationReason.FailureRollback, ct).ConfigureAwait(false);
		} catch (Exception ex) {
			rollbackErrs.Add(ExceptionSnapshot.FromException(ex));
		}

		try {
			foreach (LoadedCodeMod<TGameApi> old in transaction.OldCode.Values)
				old.OwnerScope = new OwnerScope(old.Staged.Manifest.OwnerID);
			foreach (LoadedContentMod old in transaction.OldContent.Values)
				old.OwnerScope = new OwnerScope(old.Staged.Manifest.OwnerID);

			await HookApplier<TGameApi>.ApplyLoadHooksAsync(transaction.OldCode.Values.ToArray(), maxParallelDomains, ct).ConfigureAwait(false);
			await activateSetAsync(plan.OldTouchedSet, activeGraph, activeCode, ct).ConfigureAwait(false);
		} catch (Exception ex) {
			rollbackErrs.Add(ExceptionSnapshot.FromException(ex));
		}

		if (rollbackErrs.Count > 0)
			throw new AggregateException("mod operation failed and rollback also failed", rollbackErrs.Select(static e => e.ToException()).Prepend(reloadErr.ToException()));
		ModOperationResult r = ModOperationResult.RollbackSucceeded(reloadErr, preparedUnloads);
		transaction.DropPreparedStrongReferences();
		return r;
	}

	private async ValueTask<StagedMod> stageOneAsync(ModSource source, ModManifest manifest, CancellationToken ct) {
		ulong generationVal = Interlocked.Increment(ref nextGeneration);
		ReloadGeneration generation = new(manifest.OwnerID, generationVal);
		string target = Path.Combine(cacheDir, manifest.OwnerID, generationVal.ToString("D8"));
		if (Directory.Exists(target))
			Directory.Delete(target, recursive: true);
		Directory.CreateDirectory(target);
		await copyDirectoryAsync(source.RootDirectory, target, ct).ConfigureAwait(false);
		string? assemblyPath = manifest is CodeModManifest code ? Path.Combine(target, code.EntryAssembly) : null;
		return new StagedMod(source, manifest, target, generation, assemblyPath);
	}

	private static LoadedContentMod createLoadedContentMod(StagedMod stagedMod) {
		return new LoadedContentMod {
			Staged = stagedMod,
			OwnerScope = new OwnerScope(stagedMod.Manifest.OwnerID),
			GenerationScope = new ReloadGenerationScope(stagedMod.Generation),
		};
	}

	private async ValueTask<LoadedCodeMod<TGameApi>> loadCodeModBoundedAsync(StagedMod stagedMod, CancellationToken ct) {
		await codeLoadSem.WaitAsync(ct).ConfigureAwait(false);
		try {
			return await Task.Run(() => loadCodeMod(stagedMod), ct).ConfigureAwait(false);
		} finally {
			codeLoadSem.Release();
		}
	}

	private LoadedCodeMod<TGameApi> loadCodeMod(StagedMod stagedMod) {
		CodeModManifest manifest = (CodeModManifest)stagedMod.Manifest;
		if (stagedMod.MainAssemblyPath is null || !File.Exists(stagedMod.MainAssemblyPath))
			throw new ModLoadException(manifest.OwnerID, $"entry assembly '{manifest.EntryAssembly}' not found");

		ModAlc alc = new(stagedMod.MainAssemblyPath, sharedAssemblies, $"mod:{manifest.OwnerID}:{stagedMod.Generation}");
		Assembly assembly = alc.LoadFromAssemblyPath(stagedMod.MainAssemblyPath);
		validateModAssemblyAttribute(manifest, assembly);
		Type entryType = EntrypointDiscovery.FindEntrypointType(assembly, typeof(TGameApi), stagedMod.MainAssemblyPath);
		IModEntrypoint<TGameApi> entrypoint = (IModEntrypoint<TGameApi>)Activator.CreateInstance(entryType)!;
		IModReloadEntrypoint<TGameApi>? reloadEntrypoint = null;
		if (manifest.CodeHotReload == ModCodeHotReloadLevel.Live) {
			Type reloadEntrypointType = EntrypointDiscovery.FindReloadEntrypointType(assembly, typeof(TGameApi), stagedMod.MainAssemblyPath);
			reloadEntrypoint = (IModReloadEntrypoint<TGameApi>)Activator.CreateInstance(reloadEntrypointType)!;
		}

		return new LoadedCodeMod<TGameApi> {
			Staged = stagedMod,
			AssemblyLoadContext = alc,
			Assembly = assembly,
			Entrypoint = entrypoint,
			ReloadEntrypoint = reloadEntrypoint,
			OwnerScope = new OwnerScope(manifest.OwnerID),
			GenerationScope = new ReloadGenerationScope(stagedMod.Generation),
			LoadHooks = new(),
		};
	}

	private async ValueTask runLoadAsync(LoadedCodeMod<TGameApi> mod, IReadOnlyDictionary<string, LoadedOwnerInfo> owners, CancellationToken ct) {
		TGameApi api = createApi(mod);
		ModLoadLinkContextImpl<TGameApi> context = createContext(mod, api, owners);
		await mod.Entrypoint.LoadAsync(context, ct).ConfigureAwait(false);
	}

	private async ValueTask runLinkAsync(LoadedCodeMod<TGameApi> mod, IReadOnlyDictionary<string, LoadedOwnerInfo> owners, CancellationToken ct) {
		TGameApi api = createApi(mod);
		ModLoadLinkContextImpl<TGameApi> context = createContext(mod, api, owners);
		await mod.Entrypoint.LinkAsync(context, ct).ConfigureAwait(false);
	}

	private static async ValueTask activateOneAsync(LoadedCodeMod<TGameApi> mod, CancellationToken ct) {
		await mod.Entrypoint.ActivateAsync(ct).ConfigureAwait(false);
		mod.Active = true;
	}

	private async ValueTask deactivateSetAsync(HashSet<string> set, ResolvedModGraph graph, bool reverse, CancellationToken ct) {
		IEnumerable<IReadOnlyList<string>> waves = reverse ? graph.Waves.Waves.Reverse() : graph.Waves.Waves;
		foreach (IReadOnlyList<string> wave in waves) {
			List<Task> tasks = new();
			foreach (string id in wave)
				if (set.Contains(id) && activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
					tasks.Add(mod.Entrypoint.DeactivateAsync(ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
	}

	private static async ValueTask activateSetAsync(HashSet<string> set, ResolvedModGraph graph, Dictionary<string, LoadedCodeMod<TGameApi>> source, CancellationToken ct) {
		foreach (IReadOnlyList<string> wave in graph.Waves.Waves) {
			List<Task> tasks = new();
			foreach (string id in wave)
				if (set.Contains(id) && source.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
					tasks.Add(activateOneAsync(mod, ct).AsTask());
			try {
				await Task.WhenAll(tasks).ConfigureAwait(false);
			} finally {
				tasks.Clear();
			}
		}
	}

	private async ValueTask disposeOldOwnerScopesAsync(IReadOnlySet<string> set) {
		foreach (string id in set) {
			if (activeCode.TryGetValue(id, out LoadedCodeMod<TGameApi>? mod))
				await mod.OwnerScope.DisposeAsync().ConfigureAwait(false);
			if (activeContent.TryGetValue(id, out LoadedContentMod? content))
				await content.OwnerScope.DisposeAsync().ConfigureAwait(false);
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
	private static async ValueTask destroyPreparedCodeGenerationNoUnloadAsync(LoadedCodeMod<TGameApi> mod, ReloadInvalidationReason reason, CancellationToken ct) {
		try {
			await mod.OwnerScope.DisposeAsync().ConfigureAwait(false);
		} finally {
			try {
				await mod.Entrypoint.UnloadAsync(ct).ConfigureAwait(false);
			} finally {
				await mod.GenerationScope.InvalidateAsync(reason, ct).ConfigureAwait(false);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async ValueTask destroyPreparedCodeGenerationAsync(LoadedCodeMod<TGameApi> mod, ReloadInvalidationReason reason, CancellationToken ct) {
		await destroyPreparedCodeGenerationNoUnloadAsync(mod, reason, ct);
		WeakReference weak = beginUnload(detachForUnload(mod));
		if (!probeUnload(weak, maxAttempts: MaxAlcUnloadGcAttempts))
			Console.Error.WriteLine($"warning: ALC for {mod.Staged.Manifest.OwnerID} is still alive after unload request");
	}

	private static async ValueTask destroyPreparedContentGenerationAsync(LoadedContentMod mod, ReloadInvalidationReason reason, CancellationToken ct) {
		try {
			await mod.OwnerScope.DisposeAsync().ConfigureAwait(false);
		} finally {
			await mod.GenerationScope.InvalidateAsync(reason, ct).ConfigureAwait(false);
		}
	}

	private void validateReloadCapabilities(IReadOnlySet<string> reloadSet, ReloadRequestKind request) {
		foreach (string id in reloadSet) {
			ModManifest manifest = activeGraph.Mods[id].Manifest;
			if (manifest is not CodeModManifest code)
				continue;
			if (!canSatisfyReload(code.CodeHotReload, request))
				throw new ModLoadException(id, $"code-hot-reload '{code.CodeHotReload}' cannot satisfy reload request '{request}'");
		}
	}

	private static void validateCandidateReloadCapabilities(ResolvedModGraph candidateGraph, IReadOnlySet<string> reloadSet, ReloadRequestKind request) {
		foreach (string id in reloadSet) {
			ModManifest manifest = candidateGraph.Mods[id].Manifest;
			if (manifest is not CodeModManifest code)
				continue;
			if (!canSatisfyReload(code.CodeHotReload, request))
				throw new ModLoadException(id, $"new code-hot-reload '{code.CodeHotReload}' cannot satisfy reload request '{request}'");
		}
	}

	private static bool canSatisfyReload(ModCodeHotReloadLevel level, ReloadRequestKind request) => request.Tag switch {
		ReloadRequestKind.Case.SafeBoundary => level.Tag is ModCodeHotReloadLevel.Case.SafeBoundary or ModCodeHotReloadLevel.Case.Live,
		ReloadRequestKind.Case.Live => level == ModCodeHotReloadLevel.Live,
		_ => false,
	};

	private static bool isEligibleAtBoundary(in PendingOp op, ReloadBoundaryKind boundary) => op.Kind switch {
		OpKind.Reload => op.ReloadKind is ReloadRequestKind request && boundaryAllows(boundary, request),
		OpKind.Disable => boundaryAllows(boundary, ReloadRequestKind.SafeBoundary),
		OpKind.Enable => boundaryAllows(boundary, ReloadRequestKind.SafeBoundary),
		_ => false,
	};

	private static bool boundaryAllows(ReloadBoundaryKind boundary, ReloadRequestKind request) => request.Tag switch {
		ReloadRequestKind.Case.SafeBoundary => boundary is ReloadBoundaryKind.Safe or ReloadBoundaryKind.Live,
		ReloadRequestKind.Case.Live => boundary == ReloadBoundaryKind.Live,
		_ => false,
	};

	private static void validateModAssemblyAttribute(CodeModManifest manifest, Assembly assembly) {
		ModAssemblyAttribute attribute = assembly.GetCustomAttribute<ModAssemblyAttribute>() ??
			throw new ModLoadException(manifest.OwnerID, "entry assembly is missing ModAssembly attribute");
		if (!ModMetadataValidation.ValidateOwnerID(attribute.OwnerID, out string? err))
			throw new ModLoadException(manifest.OwnerID, $"assembly attribute OwnerID '{attribute.OwnerID}' is invalid: {err}");
		if (attribute.OwnerID != manifest.OwnerID)
			throw new ModLoadException(manifest.OwnerID, $"manifest id '{manifest.OwnerID}' does not match assembly attribute OwnerID '{attribute.OwnerID}'");
		if (attribute.HotReloadLevel != (ModAssemblyHotReloadLevel)manifest.CodeHotReload)
			throw new ModLoadException(manifest.OwnerID, $"manifest code-hot-reload '{manifest.CodeHotReload}' does not match assembly attribute HotReloadLevel '{attribute.HotReloadLevel}'");
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private TGameApi createApi(LoadedCodeMod<TGameApi> mod) =>
		apiFactory(new ModApiFactoryContext(mod.Staged.Manifest.OwnerID, mod.OwnerScope));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ModLoadLinkContextImpl<TGameApi> createContext(LoadedCodeMod<TGameApi> mod, TGameApi api, IReadOnlyDictionary<string, LoadedOwnerInfo> owners) =>
		new(owners) {
			OwnerID = mod.Staged.Manifest.OwnerID,
			Version = mod.Staged.Manifest.Version,
			Api = api,
			OwnerScope = mod.OwnerScope,
			GenerationScope = mod.GenerationScope,
			UnloadToken = mod.GenerationScope.Stopping,
		};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Dictionary<string, LoadedOwnerInfo> buildOwnerInfo(IEnumerable<StagedMod> stagedMods) =>
		buildOwnerInfo(stagedMods.Select(static mod => mod.Manifest));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Dictionary<string, LoadedOwnerInfo> buildOwnerInfo(IEnumerable<ModManifest> manifests) =>
		manifests.ToDictionary(
			static manifest => manifest.OwnerID,
			static manifest => new LoadedOwnerInfo(manifest.OwnerID, manifest.Version),
			StringComparer.Ordinal
		);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void requirePhase(RuntimePhase expected, string operation) {
		if (phase != expected)
			throw new InvalidOperationException($"{operation} requires phase {expected}, but current phase is {phase}");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PendingAlcUnload detachForUnload(LoadedCodeMod<TGameApi> mod) {
		string ownerID = mod.Staged.Manifest.OwnerID;
		ModAlc alc = mod.AssemblyLoadContext;
		mod.DropStrongReferences();
		return new PendingAlcUnload(ownerID, alc);
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

	private static async ValueTask copyDirectoryAsync(string source, string target, CancellationToken ct) {
		foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));

		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			ct.ThrowIfCancellationRequested();
			string relative = Path.GetRelativePath(source, file);
			string destination = Path.Combine(target, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			await using FileStream input = File.OpenRead(file);
			await using FileStream output = File.Create(destination);
			await input.CopyToAsync(output, ct).ConfigureAwait(false);
		}
	}
}
