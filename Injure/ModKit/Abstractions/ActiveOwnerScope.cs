// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Injure.ModKit.Abstractions;

public interface IActiveOwnerScope {
	string OwnerID { get; }
	ReloadGeneration Generation { get; }
	CancellationToken RawStopping { get; }

	void AddDisposable(IDisposable disposable);
	void AddAsyncDisposable(IAsyncDisposable disposable);

	void AddOrderedDisposable(IDisposable disposable);
	void AddOrderedAsyncDisposable(IAsyncDisposable disposable);

	void Track(IReloadInvalidatable item);
	void TrackWeak(object item, string category, string description = "");
}

public interface IActiveOwnerScopeRuntimeControl : IAsyncDisposable {
	ValueTask InvalidateAsync(ReloadInvalidationReason reason, CancellationToken ct);
	IReadOnlyList<ReloadWeakReferenceSnapshot> SnapshotWeakReferences();
}

public interface IActiveOwnerScope<TLifetime> : IActiveOwnerScope where TLifetime : struct, IModLifetimeIdentity {
	GenerationCancellationToken<TLifetime> Stopping { get; }

	GenerationCancellationSource<TLifetime> CreateCancellationSource();
	GenerationCancellationSource<TLifetime> CreateLinkedCancellationSource(CancellationToken cancellationToken);
}

public readonly record struct ActiveOwnerScopeFailure(
	ReloadGeneration Generation,
	int Index,
	string Operation,
	string ItemTypeName,
	string ExceptionType,
	string Message,
	string Details
) {
	public static ActiveOwnerScopeFailure FromException(ReloadGeneration generation, int index, string operation, string itemTypeName, Exception ex) =>
		new(generation, index, operation, itemTypeName, ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.ToString());
}

public sealed class ActiveOwnerScopeException(
	ReloadGeneration generation,
	ReloadInvalidationReason reason,
	IReadOnlyList<ActiveOwnerScopeFailure> failures
) : Exception($"active owner scope invalidation failed for '{generation}' with {failures.Count} failure(s)") {
	public ReloadGeneration Generation { get; } = generation;
	public ReloadInvalidationReason Reason { get; } = reason;
	public IReadOnlyList<ActiveOwnerScopeFailure> Failures { get; } = failures;
}

public readonly record struct ReloadWeakReferenceSnapshot(
	ReloadGeneration Generation,
	string Category,
	string Description,
	bool IsAlive,
	string TargetTypeName
);

public sealed class ActiveOwnerScope : IActiveOwnerScope, IActiveOwnerScopeRuntimeControl, IAsyncDisposable {
	private readonly record struct TrackedWeakReference(WeakReference Reference, string Category, string Description);

	private struct OwnedDisposable {
		private IDisposable? disposable;
		private IAsyncDisposable? asyncDisposable;
		private readonly string? typeName;

		public OwnedDisposable(IDisposable d) {
			disposable = d;
			typeName = d.GetType().FullName ?? d.GetType().Name;
		}

		public OwnedDisposable(IAsyncDisposable ad) {
			asyncDisposable = ad;
			typeName = ad.GetType().FullName ?? ad.GetType().Name;
		}

		public readonly string TypeName => typeName ?? throw new InternalStateException("badly constructed OwnedDisposable (accidental `default`)?");

		public async ValueTask DisposeAsync() {
			IDisposable? d = disposable;
			IAsyncDisposable? ad = asyncDisposable;
			disposable = null;
			asyncDisposable = null;
			if (ad is not null) {
				await ad.DisposeAsync().ConfigureAwait(false);
				return;
			}
			(d ?? throw new InternalStateException("badly constructed OwnedDisposable (accidental `default`)?")).Dispose();
		}
	}

	private readonly Lock @lock = new();
	private readonly int maxParallelDisposals;
	private readonly CancellationTokenSource stoppingCts = new();

	private List<IReloadInvalidatable>? invalidatables = new();
	private List<OwnedDisposable>? parallel = new();
	private List<OwnedDisposable>? ordered = new();
	private List<TrackedWeakReference>? weakRefs = new();

	private bool invalidated;

	public string OwnerID => Generation.OwnerID;
	public ReloadGeneration Generation { get; }

	public bool IsInvalidated {
		get {
			lock (@lock)
				return invalidated;
		}
	}

	public CancellationToken RawStopping => stoppingCts.Token;

	public ActiveOwnerScopeView<TLifetime> AsTyped<TLifetime>() where TLifetime : struct, IModLifetimeIdentity => new(this);

	public GenerationCancellationToken<TLifetime> CreateStoppingToken<TLifetime>() where TLifetime : struct, IModLifetimeIdentity =>
		new(Generation, stoppingCts.Token);

	public GenerationCancellationSource<TLifetime> CreateCancellationSource<TLifetime>() where TLifetime : struct, IModLifetimeIdentity =>
		CreateLinkedCancellationSource<TLifetime>(CancellationToken.None);

	public GenerationCancellationSource<TLifetime> CreateLinkedCancellationSource<TLifetime>(CancellationToken ct) where TLifetime : struct, IModLifetimeIdentity {
		CancellationTokenSource linked = ct.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(stoppingCts.Token, ct)
			: CancellationTokenSource.CreateLinkedTokenSource(stoppingCts.Token);
		GenerationCancellationSourceCore source = new(Generation, linked);
		try {
			AddDisposable(source);
			return new GenerationCancellationSource<TLifetime>(source);
		} catch {
			source.Dispose();
			throw;
		}
	}

	private ActiveOwnerScope(ReloadGeneration generation, int maxParallelDisposals) {
		Generation = generation;
		this.maxParallelDisposals = Math.Max(1, maxParallelDisposals);
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	public static ActiveOwnerScope CreateRootForRuntime(
		ReloadGeneration generation,
		int maxParallelDisposals = 8
	) => new(generation, maxParallelDisposals);

	public void AddDisposable(IDisposable disposable) {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: false);
	}

	public void AddAsyncDisposable(IAsyncDisposable disposable) {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: false);
	}

	public void AddOrderedDisposable(IDisposable disposable) {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: true);
	}

	public void AddOrderedAsyncDisposable(IAsyncDisposable disposable) {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: true);
	}

	public void Track(IReloadInvalidatable item) {
		ArgumentNullException.ThrowIfNull(item);
		lock (@lock) {
			if (invalidated || invalidatables is null)
				throw new ReloadGenerationExpiredException(Generation);
			invalidatables.Add(item);
		}
	}

	public void TrackWeak(object item, string category, string description = "") {
		ArgumentNullException.ThrowIfNull(item);
		if (string.IsNullOrWhiteSpace(category))
			throw new ArgumentException("category cannot be null, empty, or whitespace", nameof(category));
		lock (@lock) {
			if (invalidated || weakRefs is null)
				throw new ReloadGenerationExpiredException(Generation);
			weakRefs.Add(new TrackedWeakReference(new WeakReference(item), category, description));
		}
	}

	private void add(OwnedDisposable disp, bool ordered) {
		lock (@lock) {
			if (invalidated || parallel is null || this.ordered is null)
				throw new ReloadGenerationExpiredException(Generation);
			if (ordered)
				this.ordered.Add(disp);
			else
				parallel.Add(disp);
		}
	}

	public IReadOnlyList<ReloadWeakReferenceSnapshot> SnapshotWeakReferences() {
		TrackedWeakReference[] snapshot;
		lock (@lock) {
			if (weakRefs is null)
				return Array.Empty<ReloadWeakReferenceSnapshot>();
			snapshot = weakRefs.ToArray();
		}

		ReloadWeakReferenceSnapshot[] result = new ReloadWeakReferenceSnapshot[snapshot.Length];
		for (int i = 0; i < snapshot.Length; i++) {
			object? target = snapshot[i].Reference.Target;
			result[i] = new ReloadWeakReferenceSnapshot(
				Generation,
				snapshot[i].Category,
				snapshot[i].Description,
				target is not null,
				target?.GetType().FullName ?? "<collected>"
			);
		}
		return result;
	}

	public ValueTask DisposeAsync() {
		return InvalidateAsync(ReloadInvalidationReason.Shutdown, CancellationToken.None);
	}

	public async ValueTask InvalidateAsync(ReloadInvalidationReason reason, CancellationToken ct) {
		IReloadInvalidatable[] inv;
		OwnedDisposable[] parr;
		OwnedDisposable[] ord;

		lock (@lock) {
			if (invalidated)
				return;
			ct.ThrowIfCancellationRequested();
			invalidated = true;
			inv = snapshotReverseAndClear(ref invalidatables);
			parr = snapshotAndClear(ref parallel);
			ord = snapshotAndClear(ref ordered);
			weakRefs?.Clear();
			weakRefs = null;
		}

		List<ActiveOwnerScopeFailure>? failures = null;
		try {
			try {
				stoppingCts.Cancel();
			} catch (Exception ex) {
				(failures ??= new()).Add(
					ActiveOwnerScopeFailure.FromException(
						Generation,
						index: -1,
						operation: "cancel generation stopping token",
						itemTypeName: "CancellationTokenSource",
						ex
					)
				);
			}

			await invalidateAsync(inv, reason, f => (failures ??= new()).Add(f)).ConfigureAwait(false);
			await disposeParallelAsync(Generation, parr, maxParallelDisposals, f => (failures ??= new()).Add(f)).ConfigureAwait(false);
			await disposeOrderedAsync(ord, f => (failures ??= new()).Add(f)).ConfigureAwait(false);
		} finally {
			Array.Clear(inv);
			Array.Clear(parr);
			Array.Clear(ord);
			stoppingCts.Dispose();
		}

		if (failures is { Count: > 0 })
			throw new ActiveOwnerScopeException(Generation, reason, failures);
	}

	private async ValueTask invalidateAsync(
		IReloadInvalidatable[] snapshot,
		ReloadInvalidationReason reason,
		Action<ActiveOwnerScopeFailure> addFailure
	) {
		ReloadInvalidationContext ctx = new() {
			OwnerID = OwnerID,
			OldGeneration = Generation,
			Reason = reason,
		};

		for (int i = 0; i < snapshot.Length; i++) {
			IReloadInvalidatable? item = snapshot[i];
			if (item is null)
				continue;

			string typeName = item.GetType().FullName ?? item.GetType().Name;
			try {
				item.Invalidate(ctx);
			} catch (Exception ex) {
				addFailure(ActiveOwnerScopeFailure.FromException(Generation, i, "invalidate item", typeName, ex));
			} finally {
				snapshot[i] = null!;
			}
		}
		await ValueTask.CompletedTask;
	}

	private static async ValueTask disposeParallelAsync(
		ReloadGeneration generation,
		OwnedDisposable[] disps,
		int maxWorkerCount,
		Action<ActiveOwnerScopeFailure> addFailure
	) {
		if (disps.Length == 0)
			return;

		Lock failureLock = new();
		int nextIndex = -1;
		int workerCount = Math.Min(maxWorkerCount, disps.Length);
		Task[] workers = new Task[workerCount];
		for (int worker = 0; worker < workerCount; worker++) {
			workers[worker] = Task.Run(async () => {
				for (;;) {
					int i = Interlocked.Increment(ref nextIndex);
					if (i >= disps.Length)
						return;
					string typeName = disps[i].TypeName;
					try {
						await disps[i].DisposeAsync().ConfigureAwait(false);
					} catch (Exception ex) {
						ActiveOwnerScopeFailure failure =
							ActiveOwnerScopeFailure.FromException(generation, i, "dispose parallel item", typeName, ex);
						lock (failureLock)
							addFailure(failure);
					} finally {
						disps[i] = default;
					}
				}
			});
		}
		await Task.WhenAll(workers).ConfigureAwait(false);
	}

	private async ValueTask disposeOrderedAsync(
		OwnedDisposable[] disps,
		Action<ActiveOwnerScopeFailure> addFailure
	) {
		for (int i = disps.Length - 1; i >= 0; i--) {
			string typeName = disps[i].TypeName;
			try {
				await disps[i].DisposeAsync().ConfigureAwait(false);
			} catch (Exception ex) {
				addFailure(ActiveOwnerScopeFailure.FromException(Generation, i, "dispose ordered item", typeName, ex));
			} finally {
				disps[i] = default;
			}
		}
	}

	private static T[] snapshotAndClear<T>(ref List<T>? list) {
		if (list is null || list.Count == 0) {
			list = null;
			return Array.Empty<T>();
		}
		T[] snapshot = list.ToArray();
		list.Clear();
		list = null;
		return snapshot;
	}

	private static T[] snapshotReverseAndClear<T>(ref List<T>? list) {
		if (list is null || list.Count == 0) {
			list = null;
			return Array.Empty<T>();
		}
		T[] snapshot = new T[list.Count];
		for (int i = 0; i < list.Count; i++)
			snapshot[i] = list[list.Count - i - 1];
		list.Clear();
		list = null;
		return snapshot;
	}
}

public sealed class ActiveOwnerScopeView<TLifetime> : IActiveOwnerScope<TLifetime> where TLifetime : struct, IModLifetimeIdentity {
	private readonly ActiveOwnerScope core;

	internal ActiveOwnerScopeView(ActiveOwnerScope core) {
		this.core = core;
	}

	public string OwnerID => core.OwnerID;
	public ReloadGeneration Generation => core.Generation;
	public CancellationToken RawStopping => core.RawStopping;
	public GenerationCancellationToken<TLifetime> Stopping => core.CreateStoppingToken<TLifetime>();
	public GenerationCancellationSource<TLifetime> CreateCancellationSource() => core.CreateCancellationSource<TLifetime>();
	public GenerationCancellationSource<TLifetime> CreateLinkedCancellationSource(CancellationToken cancellationToken) =>
		core.CreateLinkedCancellationSource<TLifetime>(cancellationToken);
	public void AddDisposable(IDisposable disposable) => core.AddDisposable(disposable);
	public void AddAsyncDisposable(IAsyncDisposable disposable) => core.AddAsyncDisposable(disposable);
	public void AddOrderedDisposable(IDisposable disposable) => core.AddOrderedDisposable(disposable);
	public void AddOrderedAsyncDisposable(IAsyncDisposable disposable) => core.AddOrderedAsyncDisposable(disposable);
	public void Track(IReloadInvalidatable item) => core.Track(item);
	public void TrackWeak(object item, string category, string description = "") => core.TrackWeak(item, category, description);
}
