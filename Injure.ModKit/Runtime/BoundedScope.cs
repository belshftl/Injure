// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Runtime;

internal readonly record struct ReloadWeakReferenceSnapshot(
	ReloadGeneration Generation,
	string Category,
	string Description,
	bool IsAlive,
	string TargetTypeName
);

internal readonly record struct BoundedScopeFailure(
	ReloadGeneration Generation,
	int Index,
	string Operation,
	string ItemTypeName,
	string ExceptionType,
	string Message,
	string Details
) {
	public static BoundedScopeFailure FromException(ReloadGeneration generation, int index, string operation, string itemTypeName, Exception ex) =>
		new(generation, index, operation, itemTypeName, ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.ToString());
}

internal sealed class BoundedScopeException(
	ReloadGeneration generation,
	ReloadTeardownReason reason,
	IReadOnlyList<BoundedScopeFailure> failures
) : Exception($"active owner scope invalidation failed for '{generation}' with {failures.Count} failure(s)") {
	public ReloadGeneration Generation { get; } = generation;
	public ReloadTeardownReason Reason { get; } = reason;
	public IReadOnlyList<BoundedScopeFailure> Failures { get; } = failures;
}

internal sealed class UntypedBoundedScope : IUntypedBoundedScope {
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

	private List<IReloadTeardown>? teardowns = new();
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

	public CancellationToken RawStopping {
		get {
			if (IsInvalidated)
				throw new InvalidOperationException("this BoundedScope has already been invalidated");
			return stoppingCts.Token;
		}
	}

	public BoundedScopeView<L> AsTyped<L>() where L : struct, IModLifetimeIdentity {
		if (IsInvalidated)
			throw new InvalidOperationException("this BoundedScope has already been invalidated");
		return new BoundedScopeView<L>(this);
	}

	public BoundedCt<L> CreateStoppingCt<L>() where L : struct, IModLifetimeIdentity {
		if (IsInvalidated)
			throw new InvalidOperationException("this BoundedScope has already been invalidated");
		return new BoundedCt<L>(Generation, stoppingCts.Token);
	}

	public BoundedCts<L> CreateCts<L>() where L : struct, IModLifetimeIdentity {
		if (IsInvalidated)
			throw new InvalidOperationException("this BoundedScope has already been invalidated");
		return CreateLinkedCts<L>(CancellationToken.None);
	}

	public BoundedCts<L> CreateLinkedCts<L>(CancellationToken ct) where L : struct, IModLifetimeIdentity {
		if (IsInvalidated)
			throw new InvalidOperationException("this BoundedScope has already been invalidated");
		CancellationTokenSource linked = ct.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(stoppingCts.Token, ct)
			: CancellationTokenSource.CreateLinkedTokenSource(stoppingCts.Token);
		BoundedCtsCore source = new(Generation, linked);
		try {
			AddDisposable(source);
			return new BoundedCts<L>(source);
		} catch {
			source.Dispose();
			throw;
		}
	}

	internal UntypedBoundedScope(ReloadGeneration generation, int maxParallelism) {
		Generation = generation;
		this.maxParallelism = Math.Max(1, maxParallelism);
	}

	public T AddTeardown<T>(T teardown) where T : notnull, IReloadTeardown {
		ArgumentNullException.ThrowIfNull(teardown);
		lock (@lock) {
			if (invalidated || teardowns is null)
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
			result[i] = new ReloadWeakReferenceSnapshot(Generation, snapshot[i].Category, snapshot[i].Description, target is not null, target?.GetType().FullName ?? "<collected>");
		}
		return result;
	}

	public async ValueTask InvalidateAsync(ReloadTeardownReason reason, CancellationToken ct) {
		IReloadTeardown[] tear;
		OwnedDisposable[] parr;
		OwnedDisposable[] ord;

		lock (@lock) {
			if (invalidated)
				return;
			ct.ThrowIfCancellationRequested();
			invalidated = true;
			tear = snapshotReverseAndClear(ref teardowns);
			parr = snapshotAndClear(ref parallel);
			ord = snapshotAndClear(ref ordered);
			weakRefs?.Clear();
			weakRefs = null;
		}

		List<BoundedScopeFailure>? failures = null;
		try {
			try {
				stoppingCts.Cancel();
			} catch (Exception ex) {
				(failures ??= new()).Add(BoundedScopeFailure.FromException(Generation, index: -1, operation: "cancel generation stopping token", itemTypeName: "CancellationTokenSource", ex));
			}

			await teardownAsync(tear, reason, maxParallelism, f => (failures ??= new()).Add(f)).ConfigureAwait(false);
			await disposeParallelAsync(parr, maxParallelism, f => (failures ??= new()).Add(f)).ConfigureAwait(false);
			await disposeOrderedAsync(ord, f => (failures ??= new()).Add(f)).ConfigureAwait(false);
		} finally {
			Array.Clear(tear);
			Array.Clear(parr);
			Array.Clear(ord);
			stoppingCts.Dispose();
		}

		if (failures is { Count: > 0 })
			throw new BoundedScopeException(Generation, reason, failures);
	}

	private async ValueTask teardownAsync(IReloadTeardown[] items, ReloadTeardownReason reason, int maxWorkerCount, Action<BoundedScopeFailure> addFailure) {
		if (items.Length == 0)
			return;

		ReloadTeardownContext ctx = new(OwnerID, Generation, reason);

		Lock failureLock = new();
		int nextIndex = -1;
		int workerCount = Math.Min(maxWorkerCount, items.Length);
		Task[] workers = new Task[workerCount];
		for (int worker = 0; worker < workerCount; worker++) {
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
						BoundedScopeFailure failure = BoundedScopeFailure.FromException(Generation, i, "tear down IReloadTeardown item", typeName, ex);
						lock (failureLock)
							addFailure(failure);
					} finally {
						items[i] = null!;
					}
				}
			});
		}
		await Task.WhenAll(workers).ConfigureAwait(false);
	}

	private async ValueTask disposeParallelAsync(OwnedDisposable[] items, int maxWorkerCount, Action<BoundedScopeFailure> addFailure) {
		if (items.Length == 0)
			return;

		Lock failureLock = new();
		int nextIndex = -1;
		int workerCount = Math.Min(maxWorkerCount, items.Length);
		Task[] workers = new Task[workerCount];
		for (int worker = 0; worker < workerCount; worker++) {
			workers[worker] = Task.Run(async () => {
				for (;;) {
					int i = Interlocked.Increment(ref nextIndex);
					if (i >= items.Length)
						return;
					string typeName = items[i].TypeName;
					try {
						await items[i].DisposeAsync().ConfigureAwait(false);
					} catch (Exception ex) {
						BoundedScopeFailure failure = BoundedScopeFailure.FromException(Generation, i, "dispose unordered IDisposable item", typeName, ex);
						lock (failureLock)
							addFailure(failure);
					} finally {
						items[i] = default;
					}
				}
			});
		}
		await Task.WhenAll(workers).ConfigureAwait(false);
	}

	private async ValueTask disposeOrderedAsync(OwnedDisposable[] items, Action<BoundedScopeFailure> addFailure) {
		for (int i = items.Length - 1; i >= 0; i--) {
			string typeName = items[i].TypeName;
			try {
				await items[i].DisposeAsync().ConfigureAwait(false);
			} catch (Exception ex) {
				addFailure(BoundedScopeFailure.FromException(Generation, i, "dispose ordered IDisposable item", typeName, ex));
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
		T[] snapshot = new T[list.Count];
		for (int i = 0; i < list.Count; i++)
			snapshot[i] = list[list.Count - i - 1];
		list.Clear();
		list = null;
		return snapshot;
	}
}

internal sealed class BoundedScopeView<L> : IBoundedScope<L> where L : struct, IModLifetimeIdentity {
	private readonly UntypedBoundedScope core;

	internal BoundedScopeView(UntypedBoundedScope core) {
		this.core = core;
		Stopping = core.CreateStoppingCt<L>();
	}

	public string OwnerID => core.OwnerID;
	public ReloadGeneration Generation => core.Generation;
	public BoundedCt<L> Stopping { get; }
	public BoundedCts<L> CreateCts() => core.CreateCts<L>();
	public BoundedCts<L> CreateLinkedCts(CancellationToken cancellationToken) =>
		core.CreateLinkedCts<L>(cancellationToken);
	public T AddTeardown<T>(T teardown) where T : notnull, IReloadTeardown => core.AddTeardown(teardown);
	public T AddDisposable<T>(T disposable) where T : notnull, IDisposable => core.AddDisposable(disposable);
	public T AddAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable => core.AddAsyncDisposable(disposable);
	public T AddOrderedDisposable<T>(T disposable) where T : notnull, IDisposable => core.AddOrderedDisposable(disposable);
	public T AddOrderedAsyncDisposable<T>(T disposable) where T : notnull, IAsyncDisposable => core.AddOrderedAsyncDisposable(disposable);
	public void TrackWeak(object item, string category, string description = "") => core.TrackWeak(item, category, description);
}
