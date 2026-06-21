// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Runtime;

internal sealed class UntypedBoundedScopeImpl : IUntypedBoundedScope {
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
	private readonly int maxParallelism;
	private readonly CancellationTokenSource stoppingCts = new();
	private readonly CancellationToken stoppingToken;

	private List<IReloadTeardown>? teardowns = new();
	private List<OwnedDisposable>? parallel = new();
	private List<OwnedDisposable>? ordered = new();
	private List<TrackedWeakReference>? weakRefs = new();

	private int invalidationClaimed;

	public ReloadGeneration Generation { get; }

	public bool IsInvalidatingOrInvalidated => stoppingToken.IsCancellationRequested;
	public CancellationToken RawStopping => stoppingToken;

	public BoundedScopeView<L> AsTyped<L>() where L : struct, IModLifetimeIdentity {
		if (IsInvalidatingOrInvalidated)
			throw new ReloadGenerationExpiredException(Generation);
		return new BoundedScopeView<L>(this);
	}

	public BoundedCts<L> CreateCts<L>() where L : struct, IModLifetimeIdentity {
		if (IsInvalidatingOrInvalidated)
			throw new ReloadGenerationExpiredException(Generation);
		return CreateLinkedCts<L>(CancellationToken.None);
	}

	public BoundedCts<L> CreateLinkedCts<L>(CancellationToken ct) where L : struct, IModLifetimeIdentity {
		if (IsInvalidatingOrInvalidated)
			throw new ReloadGenerationExpiredException(Generation);
		CancellationTokenSource linked = ct.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, ct)
			: CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
		BoundedCtsCore source = new(Generation, linked);
		try {
			AddDisposable(source);
			return new BoundedCts<L>(source);
		} catch {
			source.Dispose();
			throw;
		}
	}

	internal UntypedBoundedScopeImpl(ReloadGeneration generation, int maxParallelism) {
		Generation = generation;
		this.maxParallelism = Math.Max(1, maxParallelism);
		stoppingToken = stoppingCts.Token;
	}

	public T AddTeardown<T>(T teardown) where T : notnull, IReloadTeardown {
		ArgumentNullException.ThrowIfNull(teardown);
		if (IsInvalidatingOrInvalidated)
			throw new ReloadGenerationExpiredException(Generation);
		lock (@lock) {
			if (IsInvalidatingOrInvalidated || teardowns is null)
				throw new ReloadGenerationExpiredException(Generation);
			teardowns.Add(teardown);
			return teardown;
		}
	}

	public T AddDisposable<T>(T disposable) where T : notnull, IDisposable {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: false);
		return disposable;
	}

	public T AddAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: false);
		return disposable;
	}

	public T AddOrderedDisposable<T>(T disposable) where T : notnull, IDisposable {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: true);
		return disposable;
	}

	public T AddOrderedAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable {
		ArgumentNullException.ThrowIfNull(disposable);
		add(new OwnedDisposable(disposable), ordered: true);
		return disposable;
	}

	public void TrackWeak(object item, string category, string description = "") {
		ArgumentNullException.ThrowIfNull(item);
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		ArgumentNullException.ThrowIfNull(description);
		lock (@lock) {
			if (IsInvalidatingOrInvalidated || weakRefs is null)
				throw new ReloadGenerationExpiredException(Generation);
			weakRefs.Add(new TrackedWeakReference(new WeakReference(item), category, description));
		}
	}

	private void add(OwnedDisposable disp, bool ordered) {
		if (IsInvalidatingOrInvalidated)
			throw new ReloadGenerationExpiredException(Generation);
		lock (@lock) {
			if (IsInvalidatingOrInvalidated || parallel is null || this.ordered is null)
				throw new ReloadGenerationExpiredException(Generation);
			if (ordered)
				this.ordered.Add(disp);
			else
				parallel.Add(disp);
		}
	}

	public IReadOnlyList<ReloadWeakReferenceSnapshot> SnapshotWeakReferences() {
		if (IsInvalidatingOrInvalidated)
			return Array.Empty<ReloadWeakReferenceSnapshot>();

		TrackedWeakReference[] snapshot;
		lock (@lock) {
			if (IsInvalidatingOrInvalidated || weakRefs is null)
				return Array.Empty<ReloadWeakReferenceSnapshot>();
			snapshot = weakRefs.ToArray();
		}

		var result = new ReloadWeakReferenceSnapshot[snapshot.Length];
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

	public async ValueTask InvalidateAsync(ReloadTeardownReason reason, CancellationToken ct) {
		if (Volatile.Read(ref invalidationClaimed) != 0)
			return;
		ct.ThrowIfCancellationRequested();
		if (Interlocked.CompareExchange(ref invalidationClaimed, 1, 0) != 0)
			return;

		Task cancellationTask = stoppingCts.CancelAsync();

		IReloadTeardown[] tear;
		OwnedDisposable[] par;
		OwnedDisposable[] ord;
		lock (@lock) {
			tear = snapshotReverseAndClear(ref teardowns);
			par = snapshotAndClear(ref parallel);
			ord = snapshotAndClear(ref ordered);
			weakRefs?.Clear();
			weakRefs = null;
		}

		List<BoundedScopeFailure>? failures = null;
		try {
			try {
				await cancellationTask.ConfigureAwait(false);
			} catch (Exception ex) {
				(failures ??= []).Add(BoundedScopeFailure.FromException(Generation, index: -1, operation: "cancel generation stopping token", itemTypeName: nameof(CancellationTokenSource), ex));
			}

			await teardownAsync(tear, reason, maxParallelism, f => (failures ??= []).Add(f)).ConfigureAwait(false);
			await disposeParallelAsync(par, maxParallelism, f => (failures ??= []).Add(f)).ConfigureAwait(false);
			await disposeOrderedAsync(ord, f => (failures ??= []).Add(f)).ConfigureAwait(false);
		} finally {
			Array.Clear(tear);
			Array.Clear(par);
			Array.Clear(ord);
			stoppingCts.Dispose();
		}

		if (failures is { Count: > 0 })
			throw new BoundedScopeException(Generation, reason, failures);
	}

	private async ValueTask teardownAsync(IReloadTeardown[] items, ReloadTeardownReason reason, int maxWorkerCount, Action<BoundedScopeFailure> addFailure) {
		if (items.Length == 0)
			return;

		ReloadTeardownContext ctx = new(Generation.OwnerID, Generation, reason);

		Lock failureLock = new();
		int nextIndex = -1;
		int workerCount = Math.Min(maxWorkerCount, items.Length);
		var workers = new Task[workerCount];
		for (int worker = 0; worker < workerCount; worker++)
			workers[worker] = Task.Run(() => {
					for (;;) {
						int i = Interlocked.Increment(ref nextIndex);
						if (i >= items.Length)
							return;
						if (items[i] is null)
							continue;
						string typeName = items[i].GetType().FullName ?? items[i].GetType().Name;
						try {
							items[i].Teardown(in ctx);
						} catch (Exception ex) {
							var failure = BoundedScopeFailure.FromException(Generation, i, "tear down IReloadTeardown item", typeName, ex);
							lock (failureLock)
								addFailure(failure);
						} finally {
							items[i] = null!;
						}
					}
				}
			);
		await Task.WhenAll(workers).ConfigureAwait(false);
	}

	private async ValueTask disposeParallelAsync(OwnedDisposable[] items, int maxWorkerCount, Action<BoundedScopeFailure> addFailure) {
		if (items.Length == 0)
			return;

		Lock failureLock = new();
		int nextIndex = -1;
		int workerCount = Math.Min(maxWorkerCount, items.Length);
		var workers = new Task[workerCount];
		for (int worker = 0; worker < workerCount; worker++)
			workers[worker] = Task.Run(async () => {
					for (;;) {
						int i = Interlocked.Increment(ref nextIndex);
						if (i >= items.Length)
							return;
						string typeName = items[i].TypeName;
						try {
							await items[i].DisposeAsync().ConfigureAwait(false);
						} catch (Exception ex) {
							var failure = BoundedScopeFailure.FromException(Generation, i, "dispose parallel-disposal IDisposable item", typeName, ex);
							lock (failureLock)
								addFailure(failure);
						} finally {
							items[i] = default;
						}
					}
				}
			);
		await Task.WhenAll(workers).ConfigureAwait(false);
	}

	private async ValueTask disposeOrderedAsync(OwnedDisposable[] items, Action<BoundedScopeFailure> addFailure) {
		for (int i = items.Length - 1; i >= 0; i--) {
			string typeName = items[i].TypeName;
			try {
				await items[i].DisposeAsync().ConfigureAwait(false);
			} catch (Exception ex) {
				addFailure(BoundedScopeFailure.FromException(Generation, i, "dispose ordered-disposal IDisposable item", typeName, ex));
			} finally {
				items[i] = default;
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
		var snapshot = new T[list.Count];
		for (int i = 0; i < list.Count; i++)
			snapshot[i] = list[list.Count - i - 1];
		list.Clear();
		list = null;
		return snapshot;
	}
}

internal sealed class BoundedScopeView<L> : IBoundedScope<L> where L : struct, IModLifetimeIdentity {
	private readonly UntypedBoundedScopeImpl core;

	internal BoundedScopeView(UntypedBoundedScopeImpl core) {
		this.core = core;
		Stopping = new BoundedCt<L>(Generation, core.RawStopping);
	}

	public ReloadGeneration Generation => core.Generation;
	public bool IsInvalidatingOrInvalidated => core.IsInvalidatingOrInvalidated;
	public BoundedCt<L> Stopping { get; }
	public BoundedCts<L> CreateCts() => core.CreateCts<L>();
	public BoundedCts<L> CreateLinkedCts(CancellationToken cancellationToken) => core.CreateLinkedCts<L>(cancellationToken);
	public T AddTeardown<T>(T teardown) where T : notnull, IReloadTeardown => core.AddTeardown(teardown);
	public T AddDisposable<T>(T disposable) where T : notnull, IDisposable => core.AddDisposable(disposable);
	public T AddAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable => core.AddAsyncDisposable(disposable);
	public T AddOrderedDisposable<T>(T disposable) where T : notnull, IDisposable => core.AddOrderedDisposable(disposable);
	public T AddOrderedAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable => core.AddOrderedAsyncDisposable(disposable);
	public void TrackWeak(object item, string category, string description = "") => core.TrackWeak(item, category, description);
}
