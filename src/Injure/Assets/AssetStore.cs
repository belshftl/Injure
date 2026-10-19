// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Injure.Collections;
using Injure.Mods;
using Injure.Mods.CodeAnalysis;

namespace Injure.Assets;

internal enum AssetRegistrationKind {
	Source,
	Creator,
	DepWatcher,
}

/// <summary>
/// Handle to a registered asset pipeline component or dependency watcher in an asset store.
/// </summary>
public sealed class AssetStoreRegistration : IReloadTeardown {
	private AssetStore? store;
	private readonly ulong storeId;
	private readonly AssetRegistrationKind kind;
	private readonly ulong id;
	private Type? type;
	private int removed = 0;

	internal AssetStoreRegistration(AssetStore store, AssetRegistrationKind kind, ulong id, Type? type) {
		this.store = store;
		storeId = store.StoreId;
		this.kind = kind;
		this.id = id;
		this.type = type;
	}

	/// <summary>
	/// Removes the registration. No-op if it has already been removed.
	/// </summary>
	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	public void Remove() {
		if (Interlocked.Exchange(ref removed, 1) != 0)
			return;
		(store ?? throw new InternalStateException("is the flag guard above broken..?")).UnregisterRegistration(storeId, kind, id, type);
		store = null;
		type = null;
	}

	/// <summary>
	/// <see cref="IReloadTeardown"/> implementation; equivalent to <see cref="Remove()"/>.
	/// </summary>
	public void Teardown(in ReloadTeardownCtx ctx) => Remove();
}

/// <summary>
/// Central manager of assets: their sources, creators, and dependency watchers, their current
/// versions, and the publication and reclamation of reloaded versions.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="Asset{T}"/> refers to a slot in this store, which may have a current version, a
/// prepared replacement waiting to be published, both, or neither.
/// </para>
/// <para>
/// Publication is explicit: <see cref="Asset{T}.QueueReloadAsync(CancellationToken)"/> (or a
/// dependency watcher) prepares a replacement version, and <see cref="ApplyQueuedReloads()"/>
/// publishes every prepared replacement. Borrowing or pinning never publishes anything.
/// </para>
/// <para>
/// Replaced versions aren't reclaimed right away. Threads that borrow assets attach to the store
/// with <see cref="AttachCurrentThread()"/>, and report asset reload boundaries, points at which
/// borrows may be invalidated, with <see cref="AssetThreadAttachment.AtReloadBoundary()"/>. A
/// replaced version is reclaimed by the first <see cref="ApplyQueuedReloads()"/> call after every
/// attached thread passed a boundary since the replacement, and after every pin of it was disposed.
/// Reclamation releases the version's custody (see <see cref="IAssetStoreValue"/>), and disposes it
/// if it's an <see cref="IAssetStoreDisposable"/>; all of that happens on the thread calling
/// <see cref="ApplyQueuedReloads()"/>.
/// </para>
/// <para>
/// Using an old version is always explicit: a borrow throws once the borrowing thread passed a
/// boundary, and the only way to keep a version is an <see cref="AssetPin{T}"/>. Reporting
/// boundaries isn't necessary to observe reloads, since borrows always get the current version.
/// </para>
/// <para>
/// Published versions carry the dependencies collected while creating them. Dependency watchers
/// (see <see cref="IAssetDepWatcher{T}"/>) observe them, and queue reloads when they change.
/// </para>
/// <para>
/// Disposing the store reclaims every version it holds, <b>including pinned ones</b>.
/// </para>
/// </remarks>
public sealed class AssetStore : IDisposable {
	// ==========================================================================
	// versions/slots
	private interface IPendingAssetValue : IDisposable {
		object FinalizeValue(AssetId id);
	}

	private sealed class PendingPrepared(
		IPendingAssetValue prepared,
		ImmutableArray<IAssetDep> deps,
		ulong version,
		AssetReloadRequestOrigin origin,
		AssetDepSnapshot? trigger
	) : IDisposable {
		public readonly IPendingAssetValue Prepared = prepared;
		public readonly ImmutableArray<IAssetDep> Deps = deps;
		public readonly ulong Version = version;
		public readonly AssetReloadRequestOrigin Origin = origin;
		public readonly AssetDepSnapshot? Trigger = trigger;
		public void Dispose() => Prepared.Dispose();
	}

	/// <remarks>
	/// The <see langword="default"/> value is invalid.
	/// </remarks>
	private readonly record struct Retired(AssetVersion Version, ulong Epoch) : IHasInvalidDefault {
		bool IHasInvalidDefault.IsDefault => this == default;
	}

	internal interface IDerivedSlot {
		// marks the slot disposed and takes its current version out, for store disposal
		AssetVersion? DetachForStoreDispose();
	}

	internal interface IAssetSlot {
		AssetStore Store { get; }
		Type AssetType { get; }
		AssetId AssetId { get; }
		bool HasCurrent { get; }
		bool HasQueuedReload { get; }
		ExceptionSnapshot? LastLoadException { get; }
		AssetReloadFailure? LastReloadFailure { get; }

		Task WarmAsync(CancellationToken ct);
		Task QueueReloadAsync(CancellationToken ct);
		void QueueReloadFromDep(IAssetDep dep);
		bool Evict();
		// takes the current version and any prepared reload out of the slot, for store disposal
		AssetVersion? DetachForStoreDispose();

		void AddResident();
		void ReleaseResident();
		// evicts the slot if it's still not resident; false if a load is in progress, so it has to
		// be tried again later
		bool TryEvictUnresident(ulong epoch);

		// returns true if something actually got published
		bool ApplyQueuedReload(ulong epoch, ImmutableArray<AssetReloadFailure>.Builder failures);
	}

	// a published version of an asset or derived value; derived versions also pin the versions of
	// their dependencies they were computed from (`inputs`), and release them when reclaimed
	internal abstract class AssetVersion(AssetId? assetId, string description, ulong version, ImmutableArray<IAssetDep> deps, AssetVersion[]? inputs) {
		private int pins = 0;
		private AssetStoreCustody? custody;

		public readonly AssetId? AssetId = assetId;
		public readonly ulong Version = version;
		public readonly ImmutableArray<IAssetDep> Deps = deps;
		public readonly AssetVersion[]? Inputs = inputs;
		// 0 for asset versions, otherwise one more than the deepest input
		public readonly int Depth = inputs is null || inputs.Length == 0 ? 0 : inputs.Max(static i => i.Depth) + 1;
		public abstract object UntypedValue { get; }

		public int PinCount => Volatile.Read(ref pins);
		public void AddPin() => Interlocked.Increment(ref pins);
		public void ReleasePin() {
			if (Interlocked.Decrement(ref pins) < 0)
				throw new InternalStateException("asset version pin count went negative");
		}

		// called with the slot lock held, right before publication
		public void Adopt() {
			if (UntypedValue is IAssetStoreValue v) {
				custody = new AssetStoreCustody(AssetId, description, Version);
				v.AttachCustody(custody);
			}
		}

		public void Reclaim() {
			custody?.Release();
			try {
				(UntypedValue as IAssetStoreDisposable)?.Dispose();
			} finally {
				if (Inputs is not null)
					foreach (AssetVersion input in Inputs)
						input.ReleasePin();
			}
		}

		public string Describe() => description;
	}

	internal sealed class AssetVersion<T>(AssetId? assetId, string description, T value, ulong version, ImmutableArray<IAssetDep> deps, AssetVersion[]? inputs)
		: AssetVersion(assetId, description, version, deps, inputs) where T : class {
		public readonly T Value = value;
		public override object UntypedValue => Value;

		public static AssetVersion<T> OfAsset(AssetId id, T value, ulong version, ImmutableArray<IAssetDep> deps) =>
			new(id, $"version {version} of asset '{id}'", value, version, deps, null);
	}

	internal sealed class AssetSlot<T>(ulong slotId, AssetStore store, AssetId id) : VersionSlot<T>, IAssetSlot where T : class {
		private readonly Lock @lock = new();
		private Asset<T>? handle;

		private AssetVersion<T>? curr;
		private PendingPrepared? pending;

		private bool reloadPrepareTaskActive;
		// completed with the outcome of the newest explicit reload request; the live exception it may
		// carry is only referenced by the waiters, unlike the snapshot in lastReloadFailure
		private TaskCompletionSource? reloadOutcome;
		private Task? materializeTask;

		private ExceptionSnapshot? lastLoadException;
		private AssetReloadFailure? lastReloadFailure;
		private ExceptionSnapshot? reloadPrepareTaskEx;

		private ulong newestRequestedVersion = 1;
		private int residentCount = 0;
		private AssetReloadRequestOrigin newestReloadOrigin;
		private AssetDepSnapshot? newestReloadTrigger;

		public readonly ulong SlotId = slotId;
		public override AssetStore Store { get; } = store;
		public Type AssetType => typeof(T);
		public AssetId AssetId { get; } = id;

		public bool HasCurrent => Volatile.Read(ref curr) is not null;
		internal bool HasPendingPrepared => Volatile.Read(ref pending) is not null;
		public bool HasQueuedReload {
			get {
				lock (@lock)
					return curr is not null && pending is not null;
			}
		}
		public ExceptionSnapshot? LastLoadException => Volatile.Read(ref lastLoadException);
		public AssetReloadFailure? LastReloadFailure => Volatile.Read(ref lastReloadFailure);

		// one handle per slot, so handles compare equal by reference
		public Asset<T> Handle => Volatile.Read(ref handle) ?? Interlocked.CompareExchange(ref handle, new Asset<T>(this), null) ?? handle;

		public override bool IsCurrent(AssetVersion<T> version) => ReferenceEquals(Volatile.Read(ref curr), version);

		// a version read from `curr` stays alive until this thread's next boundary, since replacing it
		// retires it at an epoch this thread hasn't passed yet
		public AssetBorrow<T> Borrow() {
			AssetThreadAttachment attachment = Store.CurrentAttachment();
			AssetVersion<T>? ver = Volatile.Read(ref curr);
			if (ver is null) {
				reportIfNonResident();
				getOrStartMaterialize().GetAwaiter().GetResult();
				ver = Volatile.Read(ref curr) ?? throw new InternalStateException("asset materialize finished without making the curr version");
			}
			return new AssetBorrow<T>(this, ver, attachment);
		}

		public bool TryPassiveBorrow(out AssetBorrow<T> borrow) {
			AssetThreadAttachment attachment = Store.CurrentAttachment();
			AssetVersion<T>? ver = Volatile.Read(ref curr);
			borrow = ver is not null ? new AssetBorrow<T>(this, ver, attachment) : default;
			return ver is not null;
		}

		// pinning under the lock that replacing `curr` takes means a version is never pinned after it
		// got retired, so this works from unattached threads too
		public AssetPin<T> Pin() => new(this, PinCurrent());

		public override AssetVersion<T> PinCurrent() {
			for (;;) {
				Store.CheckDisposed();
				lock (@lock) {
					if (curr is not null) {
						curr.AddPin();
						return curr;
					}
				}
				reportIfNonResident();
				getOrStartMaterialize().GetAwaiter().GetResult();
			}
		}

		// a residency loads its assets ahead of time; a blocking first load of an asset nothing made
		// resident is likely a stutter
		private void reportIfNonResident() {
			bool resident;
			lock (@lock)
				resident = residentCount != 0;
			if (!resident)
				Store.ReportNonResidentLoad(new AssetKey(AssetId, typeof(T)));
		}

		// under the lock, so eviction (which checks the count under it too) can't slip in between a
		// new residency's increment and its load
		public void AddResident() {
			lock (@lock)
				residentCount++;
		}

		public void ReleaseResident() {
			bool last;
			lock (@lock) {
				if (--residentCount < 0)
					throw new InternalStateException("asset resident count went negative");
				last = residentCount == 0;
			}
			if (last)
				Store.QueueEvictCandidate(this);
		}

		public bool TryEvictUnresident(ulong epoch) {
			AssetVersion<T>? oldver;
			PendingPrepared? oldPending;
			lock (@lock) {
				if (residentCount != 0)
					return true;
				if (materializeTask is not null || reloadPrepareTaskActive)
					return false;
				oldver = curr;
				oldPending = pending;
				if (oldver is null && oldPending is null)
					return true;
				if (oldver is not null && oldPending is null)
					newestRequestedVersion++;
				curr = null;
				pending = null;
			}
			oldPending?.Dispose();
			if (oldver is not null)
				Store.QueueRetire(oldver, epoch);
			return true;
		}

		public bool TryPassivePin([NotNullWhen(true)] out AssetPin<T>? pin) {
			Store.CheckDisposed();
			lock (@lock) {
				if (curr is null) {
					pin = null;
					return false;
				}
				curr.AddPin();
				pin = new AssetPin<T>(this, curr);
				return true;
			}
		}

		public async Task WarmAsync(CancellationToken ct) {
			Store.CheckDisposed();
			ct.ThrowIfCancellationRequested();
			if (Volatile.Read(ref curr) is not null)
				return;
			await getOrStartMaterialize().WaitAsync(ct).ConfigureAwait(false);
			if (Volatile.Read(ref curr) is null)
				throw new InternalStateException("asset materialize finished without making the curr version");
		}

		public async Task QueueReloadAsync(CancellationToken ct) {
			// this is public
			Store.CheckDisposed();
			ct.ThrowIfCancellationRequested();

			Task outcome;
			bool startTask;
			lock (@lock) {
				if (curr is null)
					throw new InvalidOperationException($"asset '{AssetId}' has no current version to reload; load it first");
				startTask = requestReloadLocked(AssetReloadRequestOrigin.Explicit, null, out _);
				outcome = (reloadOutcome ??= newReloadOutcome()).Task;
			}
			if (startTask)
				startReloadPrepareLoop();
			await outcome.WaitAsync(ct).ConfigureAwait(false);
		}

		private static TaskCompletionSource newReloadOutcome() {
			TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
			// waiters may have given up (cancellation), so an unobserved failure isn't worth reporting
			tcs.Task.ContinueWith(
				static t => _ = t.Exception,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default
			);
			return tcs;
		}

		// continuations run asynchronously, so this is fine to call with the lock held
		private void completeReloadLocked(Exception? ex) {
			TaskCompletionSource? o = reloadOutcome;
			reloadOutcome = null;
			if (o is null)
				return;
			if (ex is null)
				o.TrySetResult();
			else
				o.TrySetException(ex);
		}

		public void QueueReloadFromDep(IAssetDep dep) {
			// this is internal
			bool startTask;
			lock (@lock)
				startTask = requestReloadLocked(AssetReloadRequestOrigin.Dep, dep, out _);
			if (startTask)
				startReloadPrepareLoop();
		}

		public bool Evict() {
			Store.CheckDisposed();
			AssetVersion<T>? oldver;
			PendingPrepared? oldPending;
			lock (@lock) {
				if (reloadPrepareTaskEx is not null)
					throw new InternalStateException(
						"asset reload worker faulted unexpectedly; this asset slot is now marked as poisoned",
						reloadPrepareTaskEx.ToException()
					);
				if (materializeTask is not null || reloadPrepareTaskActive)
					throw new InvalidOperationException("cannot evict while load or reload preparation is in progress");
				oldver = curr;
				oldPending = pending;
				if (oldver is null && oldPending is null && lastLoadException is null && lastReloadFailure is null)
					return false;
				if (oldver is not null && oldPending is null)
					newestRequestedVersion++;
				curr = null;
				pending = null;
				lastLoadException = null;
				lastReloadFailure = null;
			}
			oldPending?.Dispose();
			if (oldver is not null)
				Store.QueueRetire(oldver, Store.ReserveRetireEpoch());
			return true;
		}

		public AssetVersion? DetachForStoreDispose() {
			AssetVersion<T>? oldver;
			PendingPrepared? oldPending;
			lock (@lock) {
				oldver = curr;
				oldPending = pending;
				curr = null;
				pending = null;
				completeReloadLocked(new ObjectDisposedException(nameof(AssetStore)));
			}
			oldPending?.Dispose();
			return oldver;
		}

		public bool ApplyQueuedReload(ulong epoch, ImmutableArray<AssetReloadFailure>.Builder failures) {
			PendingPrepared pprep;
			lock (@lock) {
				if (pending is null)
					return false;
				if (pending.Version != newestRequestedVersion)
					throw new InternalStateException("stale pending reload was left publishable");
				pprep = pending;
				pending = null;
			}

			AssetVersion<T> newver;
			try {
				T newval = finalizePrepared<T>(AssetId, pprep.Prepared);
				newver = AssetVersion<T>.OfAsset(AssetId, newval, pprep.Version, pprep.Deps);
			} catch (Exception caught) {
				pprep.Dispose();
				AssetReloadFailure f = new(
					new AssetKey(AssetId, typeof(T)),
					pprep.Version,
					AssetReloadFailureStage.Finalize,
					pprep.Origin,
					pprep.Trigger,
					ExceptionSnapshot.FromException(caught)
				);
				lock (@lock)
					lastReloadFailure = f;
				failures.Add(f);
				Store.RecordFailedReload(f);
				return false;
			}

			AssetVersion<T>? oldver;
			lock (@lock) {
				oldver = curr;
				newver.Adopt();
				curr = newver;
				lastReloadFailure = null;
				Store.onSlotPublished(this, oldver?.Deps ?? ImmutableArray<IAssetDep>.Empty, newver.Deps);
			}

			pprep.Dispose();
			if (oldver is not null)
				Store.QueueRetire(oldver, epoch);
			return true;
		}

		private bool requestReloadLocked(AssetReloadRequestOrigin origin, IAssetDep? trigger, out ulong targetVersion) {
			if (curr is null) {
				targetVersion = 1;
				return false;
			}

			targetVersion = ++newestRequestedVersion;
			newestReloadOrigin = origin;
			newestReloadTrigger = trigger is not null ? AssetDepSnapshot.FromDep(trigger) : null;
			pending?.Dispose();
			pending = null;

			bool startTask = !reloadPrepareTaskActive;
			if (startTask) {
				if (reloadPrepareTaskEx is not null)
					throw new InternalStateException(
						"asset reload worker faulted unexpectedly; this asset slot is now marked as poisoned",
						reloadPrepareTaskEx.ToException()
					);
				reloadPrepareTaskActive = true;
			}
			return startTask;
		}

		// on the thread pool rather than inline, as sources and creators may complete synchronously, and
		// would otherwise run on the requesting thread (e.g. the main one) before the request returns
		private void startReloadPrepareLoop() => _ = Task.Run(runReloadPrepareLoopAsync);

		private async Task runReloadPrepareLoopAsync() {
			try {
				for (;;) {
					ulong targetVersion;
					AssetReloadRequestOrigin targetOrigin;
					AssetDepSnapshot? targetTrigger;

					lock (@lock) {
						// only store disposal empties the slot while a reload is being prepared
						if (curr is null) {
							reloadPrepareTaskActive = false;
							completeReloadLocked(new ObjectDisposedException(nameof(AssetStore)));
							return;
						}
						targetVersion = newestRequestedVersion;
						targetOrigin = newestReloadOrigin;
						targetTrigger = newestReloadTrigger;
					}

					PendingPrepared prepared;
					try {
						(IPendingAssetValue pendingAssetValue, ImmutableArray<IAssetDep> deps) =
							await Store.tryPrepareValueAsync<T>(AssetId).ConfigureAwait(false);
						prepared = new PendingPrepared(pendingAssetValue, deps, targetVersion, targetOrigin, targetTrigger);
					} catch (Exception caught) when (!ExceptionPolicy.IsInternalState(caught)) {
						lock (@lock) {
							if (targetVersion == newestRequestedVersion) {
								AssetReloadFailure f = new(
									new AssetKey(AssetId, typeof(T)),
									targetVersion,
									AssetReloadFailureStage.Prepare,
									targetOrigin,
									targetTrigger,
									ExceptionSnapshot.FromException(caught)
								);
								lastReloadFailure = f;
								reloadPrepareTaskActive = false;
								Store.RecordFailedReload(f);
								completeReloadLocked(caught);
								return;
							}
						}
						continue;
					}

					lock (@lock) {
						if (targetVersion == newestRequestedVersion) {
							pending?.Dispose();
							pending = prepared;
							lastReloadFailure = null;
							reloadPrepareTaskActive = false;
							completeReloadLocked(null);
							return;
						}
					}
					prepared.Dispose();
				}
			} catch (Exception caught) {
				lock (@lock) {
					reloadPrepareTaskEx = ExceptionSnapshot.FromException(caught);
					reloadPrepareTaskActive = false;
					completeReloadLocked(new InternalStateException("asset reload worker faulted unexpectedly; this asset slot is now marked as poisoned", caught));
				}
			}
		}

		private async Task runMaterializeAsync(TaskCompletionSource<object?> tcs) {
			IPendingAssetValue? pendingValue = null;
			T? newval = null;
			try {
				ulong version;
				lock (@lock)
					version = newestRequestedVersion;

				(pendingValue, ImmutableArray<IAssetDep> deps) =
					await Store.tryPrepareValueAsync<T>(AssetId).ConfigureAwait(false);
				newval = finalizePrepared<T>(AssetId, pendingValue);
				var newver = AssetVersion<T>.OfAsset(AssetId, newval, version, deps);

				lock (@lock) {
					materializeTask = null;
					// a store disposed meanwhile doesn't get the value; the finally below disposes it
					if (Store.IsDisposed) {
						tcs.SetException(new ObjectDisposedException(nameof(AssetStore)));
						return;
					}
					if (curr is null) {
						if (version != newestRequestedVersion)
							throw new InternalStateException("materialized asset version became stale before publication");
						newver.Adopt();
						curr = newver;
						lastLoadException = null;
						newval = null;
						Store.onSlotPublished(this, ImmutableArray<IAssetDep>.Empty, newver.Deps);
					}
					tcs.SetResult(null);
				}
			} catch (Exception caught) {
				lock (@lock) {
					lastLoadException = ExceptionSnapshot.FromException(caught);
					materializeTask = null;
					tcs.SetException(caught);
				}
			} finally {
				pendingValue?.Dispose();
				(newval as IDisposable)?.Dispose();
			}
		}

		private Task getOrStartMaterialize() {
			lock (@lock) {
				Task task = getOrStartMaterializeLocked(out TaskCompletionSource<object?>? startMaterialize);
				if (startMaterialize is not null)
					// not inline, as that would run sources and creators under this lock whenever they complete
					// synchronously
					_ = Task.Run(() => runMaterializeAsync(startMaterialize));
				return task;
			}
		}

		private Task getOrStartMaterializeLocked(out TaskCompletionSource<object?>? startMaterialize) {
			startMaterialize = null;

			if (curr is not null)
				return Task.CompletedTask;
			if (materializeTask is not null)
				return materializeTask;
			startMaterialize = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			materializeTask = startMaterialize.Task;
			return materializeTask;
		}
	}

	// ==========================================================================
	// creation pipeline boilerplate wrappers
	private sealed class DirectPendingAssetValue<T>(T val) : IPendingAssetValue where T : class {
		private T? val = val;

		public object FinalizeValue(AssetId id) {
			T ret = val ?? throw new InternalStateException("direct asset value finalized twice");
			val = null;
			return ret;
		}

		public void Dispose() {
			(val as IDisposable)?.Dispose();
			val = null;
		}
	}

	private sealed class StagedPendingAssetValue<T, TPrepared>(IAssetStagedCreator<T, TPrepared> creator, TPrepared prepared)
		: IPendingAssetValue where T : class where TPrepared : AssetPreparedData {
		private readonly IAssetStagedCreator<T, TPrepared> creator = creator;
		private TPrepared? prepared = prepared;

		public object FinalizeValue(AssetId id) {
			TPrepared p = prepared ?? throw new InternalStateException("staged asset value finalized twice");
			prepared = null;
			try {
				return creator.Finalize(new AssetFinalizeInfo<TPrepared>(id, p));
			} finally {
				p.Dispose();
			}
		}

		public void Dispose() {
			(prepared as IDisposable)?.Dispose();
			prepared = null;
		}
	}

	/// <remarks>
	/// The <see langword="default"/> value is valid and is a
	/// <see cref="AssetCreateResultKind.NotHandled"/> result.
	/// </remarks>
	private readonly record struct UntypedCreateResult(
		AssetCreateResultKind Kind,
		IPendingAssetValue? Value = null
	) {
		public static UntypedCreateResult NotHandled() => new(AssetCreateResultKind.NotHandled);
		public static UntypedCreateResult Success(IPendingAssetValue value) => new(AssetCreateResultKind.Success, value);
	}

	private interface IUntypedAssetCreator {
		Type AssetType { get; }
		ValueTask<UntypedCreateResult> TryCreateAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct);
	}

	private sealed class UntypedDirectAssetCreator<T>(IAssetCreator<T> creator) : IUntypedAssetCreator where T : class {
		private readonly IAssetCreator<T> creator = creator;
		public Type AssetType => typeof(T);
		public async ValueTask<UntypedCreateResult> TryCreateAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct) {
			AssetCreateResult<T> result = await creator.TryCreateAsync(info, coll, ct).ConfigureAwait(false);
			return result.Kind.Tag switch {
				AssetCreateResultKind.Case.NotHandled => UntypedCreateResult.NotHandled(),
				AssetCreateResultKind.Case.Success => UntypedCreateResult.Success(
					new DirectPendingAssetValue<T>(
						result.Value ?? throw new AssetLoadException(info.AssetId, typeof(T), "asset creator returned Success but didn't set Value")
					)
				),
				_ => throw new UnreachableException(),
			};
		}
	}

	private sealed class UntypedStagedAssetCreator<T, TPrepared>(IAssetStagedCreator<T, TPrepared> creator)
		: IUntypedAssetCreator where T : class where TPrepared : AssetPreparedData {
		private readonly IAssetStagedCreator<T, TPrepared> creator = creator;
		public Type AssetType => typeof(T);
		public async ValueTask<UntypedCreateResult> TryCreateAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct) {
			AssetPrepareResult<TPrepared> result = await creator.TryPrepareAsync(info, coll, ct).ConfigureAwait(false);
			return result.Kind.Tag switch {
				AssetCreateResultKind.Case.NotHandled => UntypedCreateResult.NotHandled(),
				AssetCreateResultKind.Case.Success => UntypedCreateResult.Success(
					new StagedPendingAssetValue<T, TPrepared>(
						creator,
						result.Prepared ?? throw new AssetLoadException(info.AssetId, typeof(T), "asset staged creator returned Success but didn't set Prepared")
					)
				),
				_ => throw new UnreachableException(),
			};
		}
	}

	// ==========================================================================
	// dependency tracking (incl. boilerplate wrappers)
	private sealed class DepCollector : IAssetDepCollector {
		private readonly HashSet<IAssetDep> deps = new();
		public void Add(IAssetDep dep) {
			ArgumentNullException.ThrowIfNull(dep);
			deps.Add(dep);
		}
		public ImmutableArray<IAssetDep> Snapshot() => [.. deps];
		public void MergeFrom(DepCollector child) {
			foreach (IAssetDep dep in child.deps)
				deps.Add(dep);
		}
	}

	private interface IUntypedAssetDepWatcher : IDisposable {
		Type DepType { get; }
		void Watch(IAssetDep dep);
		void Unwatch(IAssetDep dep);
		event Action<IAssetDep> Changed;
	}

	private sealed class UntypedAssetDepWatcher<T> : IUntypedAssetDepWatcher where T : IAssetDep {
		private readonly IAssetDepWatcher<T> inner;

		public Type DepType => typeof(T);
		public event Action<IAssetDep>? Changed;

		public UntypedAssetDepWatcher(IAssetDepWatcher<T> inner) {
			this.inner = inner;
			this.inner.Changed += onChanged;
		}
		private void onChanged(T dep) => Changed?.Invoke(dep);

		public void Watch(IAssetDep dep) => inner.Watch((T)dep);
		public void Unwatch(IAssetDep dep) => inner.Unwatch((T)dep);
		public void Dispose() {
			inner.Changed -= onChanged;
			inner.Dispose();
		}
	}

	// ==========================================================================
	// identification
	private static ulong nextStoreId = 0; // first ID will be 1 since this gets incremented upfront
	internal readonly ulong StoreId = Interlocked.Increment(ref nextStoreId);

	// ==========================================================================
	// general bookkeeping
	private readonly ConcurrentDictionary<AssetKey, IAssetSlot> slots = new();
	private ulong nextSlotId = 0; // first ID will be 1 since this gets incremented upfront

	// ==========================================================================
	// creation pipeline bookkeeping
	private readonly Lock registryLock = new();
	private readonly UnsafeOwnerOrderedRegistry<IAssetSource> sources = new();
	private ImmutableDictionary<Type, UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>> creators = ImmutableDictionary<Type, UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>>.Empty;

	// ==========================================================================
	// publication / reclamation / thread attachment bookkeeping
	private readonly Lock retiredLock = new();
	private readonly List<Retired> retired = new();
	private ulong publishedEpoch = 0;
	internal ulong GetPublishedEpoch() => Volatile.Read(ref publishedEpoch);

	[ThreadStatic] private static Dictionary<ulong, AssetThreadAttachment>? tlsAttachmentsByStoreId;
	// fast path for borrows, which look up the current thread's attachment every time
	[ThreadStatic] private static AssetThreadAttachment? tlsLastAttachment;
	private readonly ConcurrentDictionary<ulong, AssetThreadAttachment> attachedThreadsById = new();

	private int disposed = 0;
	internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

	private readonly ConcurrentDictionary<IDerivedSlot, byte> derivedSlots = new();
	internal void RegisterDerived(IDerivedSlot slot) => derivedSlots.TryAdd(slot, 0);

	// slots whose last residency was released, evicted by the next ApplyQueuedReloads
	private readonly ConcurrentDictionary<IAssetSlot, byte> evictCandidates = new();
	internal void QueueEvictCandidate(IAssetSlot slot) => evictCandidates.TryAdd(slot, 0);

	/// <summary>
	/// Raised when a borrow or pin has to load an asset synchronously, and no residency includes it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Since such a load blocks, it's likely to surface as a microstutter mid-gameplay; making the
	/// asset's group resident beforehand (see <see cref="AssetResidency"/>) avoids that.
	/// </para>
	/// <para>
	/// Raised on the borrowing thread before the load starts. Handlers must not throw or call into
	/// this store; calling into this store may deadlock or recurse without end.
	/// </para>
	/// </remarks>
	public event Action<AssetKey>? NonResidentLoad;

	internal void ReportNonResidentLoad(AssetKey key) => NonResidentLoad?.Invoke(key);
	internal void UnregisterDerived(IDerivedSlot slot) => derivedSlots.TryRemove(slot, out _);

	// ==========================================================================
	// dependency bookkeeping
	private readonly Lock depLock = new();
	private Dictionary<Type, UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>> watchers = new();
	private readonly Dictionary<IAssetDep, HashSet<IAssetSlot>> slotsByDep = new();

	// ==========================================================================
	// log/report/failure bookkeeping
	private readonly Lock reloadFailureLock = new();
	private readonly Ring<AssetReloadFailure> reloadFailures = new(MaxBufferedReloadFailures);

	/// <summary>
	/// The maximum number of reload failure records retained by this store's reload-failure buffer.
	/// </summary>
	/// <remarks>
	/// Once the buffer is full, recording another reload failure discards the oldest retained failure.
	/// This buffer is diagnostic state only; per-asset failure state is exposed through
	/// <see cref="Asset{T}.LastReloadFailure"/>.
	/// </remarks>
	public const int MaxBufferedReloadFailures = 1024;

	// ==========================================================================
	// public api

	/// <summary>
	/// Gets the handle for an asset.
	/// </summary>
	/// <typeparam name="T">Asset type, which decides what creators are tried.</typeparam>
	/// <param name="id">Asset ID.</param>
	/// <returns>
	/// The handle; every call with the same ID and type returns the same <see cref="Asset{T}"/>.
	/// </returns>
	/// <remarks>
	/// Nothing is loaded until the asset is borrowed, pinned, or warmed.
	/// </remarks>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this store has been disposed.
	/// </exception>
	public Asset<T> GetAsset<T>(AssetId id) where T : class {
		CheckDisposed();
		ArgumentInvalidDefaultException.ThrowIfDefault(id);
		AssetKey key = new(id, typeof(T));
		if (!slots.TryGetValue(key, out IAssetSlot? s)) {
			IAssetSlot @new = new AssetSlot<T>(Interlocked.Increment(ref nextSlotId), this, id);
			s = slots.GetOrAdd(key, @new);
		}
		return ((AssetSlot<T>)s).Handle;
	}

	/// <summary>
	/// Attaches the current thread to this store, so it can borrow assets.
	/// </summary>
	/// <returns>
	/// The attachment, owned by the caller and usable only from the current thread. Disposing it
	/// detaches the thread.
	/// </returns>
	/// <remarks>
	/// A thread can be attached to several stores at once. An attached thread holds back reclamation
	/// of replaced versions until it reports an asset reload boundary, so threads that don't borrow
	/// shouldn't attach; they can use pins instead.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the current thread is already attached to this store.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this store has been disposed.
	/// </exception>
	public AssetThreadAttachment AttachCurrentThread() {
		CheckDisposed();
		tlsAttachmentsByStoreId ??= new Dictionary<ulong, AssetThreadAttachment>();
		if (tlsAttachmentsByStoreId.ContainsKey(StoreId))
			throw new InvalidOperationException("current thread is already attached to this AssetStore");
		AssetThreadAttachment attachment = new(this);
		Volatile.Write(ref attachment.QuiescentEpoch, Volatile.Read(ref publishedEpoch));
		if (!attachedThreadsById.TryAdd(attachment.Id, attachment))
			throw new InternalStateException("duplicate asset thread attachment id");
		tlsAttachmentsByStoreId.Add(StoreId, attachment);
		return attachment;
	}

	/// <summary>
	/// Reports an asset reload boundary for the current thread; shorthand for
	/// <see cref="AssetThreadAttachment.AtReloadBoundary()"/> on its attachment.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the current thread isn't attached to this store.
	/// </exception>
	public void AtReloadBoundary() => CurrentAttachment().AtReloadBoundary();

	/// <summary>
	/// Publishes every prepared reload, evicts assets whose last residency was released (see
	/// <see cref="AssetResidency"/>), then reclaims replaced versions that nothing can use anymore.
	/// </summary>
	/// <returns>The number of published reloads, and the reloads that failed to finalize.</returns>
	/// <remarks>
	/// <para>
	/// This doesn't start loading or preparing anything; it publishes replacements that
	/// <see cref="Asset{T}.QueueReloadAsync(CancellationToken)"/> or a dependency watcher already
	/// prepared. Publication is immediate, so borrows after this call get the new versions.
	/// </para>
	/// <para>
	/// A reload that fails to finalize leaves the previous version current; the failure is both
	/// returned and recorded in the store's reload-failure buffer.
	/// </para>
	/// <para>
	/// A replaced version is reclaimed once every attached thread reported an asset reload boundary
	/// since it was replaced, and no pins of it remain. This is the only place reclamation happens, so
	/// it always runs on the thread calling this, typically the main thread, right after its own
	/// boundary.
	/// </para>
	/// </remarks>
	/// <exception cref="AggregateException">
	/// Thrown if disposing reclaimed versions threw; every eligible version is still reclaimed, and
	/// every prepared reload is still published.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this store has been disposed.
	/// </exception>
	public AssetReloadReport ApplyQueuedReloads() {
		CheckDisposed();
		ImmutableArray<AssetReloadFailure>.Builder failures = ImmutableArray.CreateBuilder<AssetReloadFailure>();
		ulong epoch = ReserveRetireEpoch();
		int n = 0;
		foreach (IAssetSlot s in slots.Values.ToArray())
			if (s.ApplyQueuedReload(epoch, failures))
				n++;
		// removed before evicting, so a release meanwhile re-adds it rather than getting lost
		foreach (IAssetSlot s in evictCandidates.Keys) {
			evictCandidates.TryRemove(s, out _);
			if (!s.TryEvictUnresident(epoch))
				evictCandidates.TryAdd(s, 0);
		}
		collectRetired();
		return new AssetReloadReport(n, failures.ToImmutable());
	}

	/// <summary>
	/// Finalizes and applies all queued asset reloads, throwing if any of them failed.
	/// </summary>
	/// <returns>The number of assets that published a new live version.</returns>
	/// <remarks>
	/// Convenience wrapper over <see cref="ApplyQueuedReloads()"/>. Has equivalent throwing behavior
	/// to <see cref="AssetReloadReport.ThrowIfFailed()"/>.
	/// </remarks>
	/// <exception cref="AggregateException">
	/// Thrown if finalization of one or more prepared reloads failed.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this store has been disposed.
	/// </exception>
	public int ApplyQueuedReloadsOrThrow() {
		AssetReloadReport r = ApplyQueuedReloads();
		r.ThrowIfFailed();
		return r.AppliedCount;
	}

	/// <summary>
	/// Drains this store's buffered reload failures.
	/// </summary>
	/// <returns>
	/// The reload failures that had been recorded by this store in oldest-to-newest order.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Draining clears the store-level reload failure buffer, intended for diagnostics such as logging
	/// / editor UIs / debug overlays / etc. It is not needed to determine the current state of a
	/// specific asset; use <see cref="Asset{T}.LastReloadFailure"/> for that.
	/// </para>
	/// <para>
	/// Dependency-triggered reload failures are recorded here because they do not throw into the
	/// dependency watcher event path. Finalize failures from <see cref="ApplyQueuedReloads()"/> are
	/// also recorded here.
	/// </para>
	/// </remarks>
	public AssetReloadFailure[] DrainReloadFailures() {
		lock (reloadFailureLock) {
			AssetReloadFailure[] ret = reloadFailures.ToArray();
			reloadFailures.Clear();
			return ret;
		}
	}

	/// <summary>
	/// Registers an asset source.
	/// </summary>
	/// <returns>
	/// A registration handle that can be used to remove the registration.
	/// </returns>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetStoreRegistration RegisterSource(OwnerOrderedEntry<IAssetSource> source) {
		ArgumentNullException.ThrowIfNull(source);
		lock (registryLock) {
			ulong id = sources.RegisterLocked(source);
			return new AssetStoreRegistration(this, AssetRegistrationKind.Source, id, null);
		}
	}

	private static void chkAssetType(Type type, string paramName) {
		if (typeof(IDisposable).IsAssignableFrom(type) && !typeof(IAssetStoreDisposable).IsAssignableFrom(type))
			throw new ArgumentException(
				$"asset type {type.FullName} implements IDisposable but not IAssetStoreDisposable, so nothing would stop its values from being disposed while the store owns them",
				paramName
			);
	}

	private AssetStoreRegistration registerCreatorLocked(OwnerOrderedEntry<IUntypedAssetCreator> creator, Type type) {
		ImmutableDictionary<Type, UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>> old = creators;
		ulong id;
		if (old.TryGetValue(type, out UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>? reg)) {
			id = reg.RegisterLocked(creator);
		} else {
			reg = new UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>();
			id = reg.RegisterLocked(creator);
			Volatile.Write(ref creators, old.Add(type, reg));
		}
		return new AssetStoreRegistration(this, AssetRegistrationKind.Creator, id, type);
	}

	/// <summary>
	/// Registers an asset creator of a specific asset type.
	/// </summary>
	/// <typeparam name="T">Asset type produced by the creator.</typeparam>
	/// <returns>
	/// A registration handle that can be used to remove the registration.
	/// </returns>
	/// <remarks>
	/// Values the creator produces must follow the same rule as <typeparamref name="T"/>: a value
	/// implementing <see cref="IDisposable"/> but not <see cref="IAssetStoreDisposable"/> fails to
	/// load.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <typeparamref name="T"/> implements <see cref="IDisposable"/> but not
	/// <see cref="IAssetStoreDisposable"/>.
	/// </exception>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetStoreRegistration RegisterCreator<T>(OwnerOrderedEntry<IAssetCreator<T>> creator) where T : class {
		ArgumentNullException.ThrowIfNull(creator);
		chkAssetType(typeof(T), nameof(creator));
		lock (registryLock)
			return registerCreatorLocked(creator.Transform<IUntypedAssetCreator>(
				static c => new UntypedDirectAssetCreator<T>(c)
			), typeof(T));
	}

	/// <summary>
	/// Registers a staged asset creator of a specific asset type.
	/// </summary>
	/// <typeparam name="T">Asset type produced by the creator.</typeparam>
	/// <typeparam name="TPrepared">Prepared-data intermediate type used by the creator.</typeparam>
	/// <inheritdoc cref="RegisterCreator{T}(OwnerOrderedEntry{IAssetCreator{T}})"/>
	public AssetStoreRegistration RegisterStagedCreator<T, TPrepared>(
		OwnerOrderedEntry<IAssetStagedCreator<T, TPrepared>> creator
	) where T : class where TPrepared : AssetPreparedData {
		ArgumentNullException.ThrowIfNull(creator);
		chkAssetType(typeof(T), nameof(creator));
		lock (registryLock)
			return registerCreatorLocked(creator.Transform<IUntypedAssetCreator>(
				static c => new UntypedStagedAssetCreator<T, TPrepared>(c)
			), typeof(T));
	}

	/// <summary>
	/// Registers a watcher for a specific dependency type.
	/// </summary>
	/// <typeparam name="TDep">Dependency type handled by the watcher.</typeparam>
	/// <returns>
	/// A registration handle that can be used to remove the registration.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Newly registered watchers are immediately subscribed to all currently published dependencies of
	/// the matching type that are already known to this store.
	/// </para>
	/// <para>
	/// Unsubscription is done in reverse order; the last-ordered watcher, i.e the last one to have
	/// been subscribed to a new dependency, will also be the first to be unsubscribed from it.
	/// </para>
	/// </remarks>
	/// <exception cref="OwnerOrderingException">
	/// Thrown if the new ordering constraints are invalid or unsatisfiable.
	/// </exception>
	public AssetStoreRegistration RegisterDepWatcher<TDep>(
		OwnerOrderedEntry<IAssetDepWatcher<TDep>> watcher
	) where TDep : IAssetDep {
		ArgumentNullException.ThrowIfNull(watcher);
		lock (depLock) {
			UntypedAssetDepWatcher<TDep> untyped = new(watcher.Item);
			OwnerOrderedEntry<IUntypedAssetDepWatcher> ent = watcher.Substitute<IUntypedAssetDepWatcher>(untyped);
			Dictionary<Type, UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>> old = watchers;
			ulong id;
			if (old.TryGetValue(typeof(TDep), out UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>? reg)) {
				id = reg.RegisterLocked(ent);
			} else {
				reg = new UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>();
				id = reg.RegisterLocked(ent);
				Dictionary<Type, UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>> @new = new(old) {
					[typeof(TDep)] = reg,
				};
				Volatile.Write(ref watchers, @new);
			}
			untyped.Changed += onDepChanged;
			foreach ((IAssetDep dep, _) in slotsByDep)
				if (dep.GetType() == typeof(TDep))
					watcher.Item.Watch((TDep)dep);
			return new AssetStoreRegistration(this, AssetRegistrationKind.DepWatcher, id, typeof(TDep));
		}
	}

	/// <summary>
	/// Unregisters an asset source by owner ID + local ID instead of by registration handle. This may
	/// break code that expects that source to be there since it's seemingly going to vanish for no
	/// reason, so use with care.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the ID pair pointed to a registered source that was, as such,
	/// successfully unregistered; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// The intended usecase is the handle being lost/unreachable but the registration needing to be
	/// removed.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID or <paramref name="localId"/> is
	/// not a valid local ID.
	/// </exception>
	public bool DangerousUnregisterSourceByIdPair(string ownerId, string localId) {
		lock (registryLock)
			return sources.UnregisterByOwnerAndLocalIdsLocked(ownerId, localId, out _);
	}

	/// <summary>
	/// Unregisters <b>every</b> source registered under the specified owner ID. This may break code
	/// that expects these sources to be there since they're seemingly going to vanish for no reason,
	/// so use with care.
	/// </summary>
	/// <remarks>
	/// The intended usecase is the owner being gone but not having properly cleaned up its
	/// registrations.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID.
	/// </exception>
	public int DangerousUnregisterAllSourcesFrom(string ownerId) {
		lock (registryLock)
			return sources.UnregisterAllByOwnerIdLocked(ownerId).Length;
	}

	/// <summary>
	/// Unregisters an asset creator for the given asset type by owner ID + local ID instead of by
	/// registration handle. This may break code that expects that creator to be there since it's
	/// seemingly going to vanish for no reason, so use with care.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the type and the ID pair pointed to a registered creator that was, as
	/// such, successfully unregistered; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// The intended usecase is the handle being lost/unreachable but the registration needing to be
	/// removed.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID or <paramref name="localId"/> is
	/// not a valid local ID.
	/// </exception>
	public bool DangerousUnregisterCreatorByIdPair<T>(string ownerId, string localId) {
		lock (registryLock) {
			if (!creators.TryGetValue(typeof(T), out UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>? reg))
				return false;
			return reg.UnregisterByOwnerAndLocalIdsLocked(ownerId, localId, out _);
		}
	}

	/// <summary>
	/// Unregisters <b>every</b> creator of every asset type registered under the specified owner ID.
	/// This may break code that expects these creators to be there since they're seemingly going to
	/// vanish for no reason, so use with care.
	/// </summary>
	/// <returns>
	/// The amount of registrations that got removed.
	/// </returns>
	/// <remarks>
	/// The intended usecase is the owner being gone but not having properly cleaned up its
	/// registrations.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID.
	/// </exception>
	public int DangerousUnregisterAllCreatorsFrom(string ownerId) {
		lock (registryLock) {
			int removed = 0;
			foreach (UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>? reg in creators.Values)
				removed += reg.UnregisterAllByOwnerIdLocked(ownerId).Length;
			return removed;
		}
	}

	/// <summary>
	/// Unregisters an asset dependency watcher for the given dependency type by owner ID + local ID
	/// instead of by registration handle. This may break code that expects that watcher to be there
	/// since it's seemingly going to vanish for no reason, so use with care.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the type and the ID pair pointed to a registered watcher that was, as
	/// such, successfully unregistered; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// The intended usecase is the handle being lost/unreachable but the registration needing to be
	/// removed.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID or <paramref name="localId"/> is
	/// not a valid local ID.
	/// </exception>
	public bool DangerousUnregisterDepWatcherByIdPair<TDep>(string ownerId, string localId) where TDep : IAssetDep {
		lock (depLock) {
			if (!watchers.TryGetValue(typeof(TDep), out UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>? reg))
				return false;
			bool ret = reg.UnregisterByOwnerAndLocalIdsLocked(ownerId, localId, out OwnerOrderedEntry<IUntypedAssetDepWatcher>? ent);
			ent?.Item.Dispose();
			return ret;
		}
	}

	/// <summary>
	/// Unregisters <b>every</b> dependency watcher of every dependency type registered under the
	/// specified owner ID. This may break code that expects these watchers to be there since they're
	/// seemingly going to vanish for no reason, so use with care.
	/// </summary>
	/// <returns>
	/// The amount of registrations that got removed.
	/// </returns>
	/// <remarks>
	/// The intended usecase is the owner being gone but not having properly cleaned up its
	/// registrations.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID.
	/// </exception>
	public int DangerousUnregisterAllDepWatchersFrom(string ownerId) {
		lock (depLock) {
			int removed = 0;
			foreach (UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>? reg in watchers.Values) {
				OwnerOrderedEntry<IUntypedAssetDepWatcher>[] ents = reg.UnregisterAllByOwnerIdLocked(ownerId);
				foreach (OwnerOrderedEntry<IUntypedAssetDepWatcher> ent in ents)
					ent.Item.Dispose();
				removed += ents.Length;
			}
			return removed;
		}
	}

	// ==========================================================================
	// internal api
	// the attachment checks that it's used from its own thread, so the TLS entry is always there
	internal void DetachThread(AssetThreadAttachment attachment) {
		attachedThreadsById.TryRemove(attachment.Id, out _);
		if (tlsAttachmentsByStoreId is null || !tlsAttachmentsByStoreId.Remove(StoreId))
			throw new InternalStateException("detached an asset thread attachment that wasn't in the thread's attachment table");
		if (ReferenceEquals(tlsLastAttachment, attachment))
			tlsLastAttachment = null;
	}

	internal AssetThreadAttachment CurrentAttachment() {
		CheckDisposed();
		AssetThreadAttachment? last = tlsLastAttachment;
		if (last is not null && ReferenceEquals(last.Store, this))
			return last;
		if (tlsAttachmentsByStoreId is null || !tlsAttachmentsByStoreId.TryGetValue(StoreId, out AssetThreadAttachment? attachment))
			throw new InvalidOperationException(
				"the current thread isn't attached to this AssetStore, which borrowing requires; attach it with AttachCurrentThread(), or use a pin instead"
			);
		tlsLastAttachment = attachment;
		return attachment;
	}

	internal void CheckDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

	internal void RecordFailedReload(AssetReloadFailure f) {
		lock (reloadFailureLock)
			reloadFailures.PushNewest(f);
	}

	internal ulong ReserveRetireEpoch() => Interlocked.Increment(ref publishedEpoch);

	internal void QueueRetire(AssetVersion version, ulong epoch) {
		lock (retiredLock)
			retired.Add(new Retired(version, epoch));
	}

	// a version is reclaimable once every attached thread passed a boundary after its retirement
	// (so no borrows of it exist, and none can be upgraded to pins anymore) and its pins are gone;
	// checking the epoch before the pin count is what makes that order safe
	private void collectRetired() {
		ulong cutoff = ulong.MaxValue;
		foreach (AssetThreadAttachment attachment in attachedThreadsById.Values)
			cutoff = Math.Min(Volatile.Read(ref attachment.QuiescentEpoch), cutoff);
		// reclaiming a derived version releases its pins of its inputs, which may make those
		// reclaimable too, so repeat until nothing changes
		List<Exception>? errors = null;
		for (;;) {
			List<AssetVersion>? reclaim = null;
			lock (retiredLock) {
				retired.RemoveAll(r => {
					if (r.Epoch > cutoff || r.Version.PinCount != 0)
						return false;
					(reclaim ??= new List<AssetVersion>()).Add(r.Version);
					return true;
				});
			}
			if (reclaim is null)
				break;
			try {
				reclaimAll(reclaim);
			} catch (AggregateException ex) {
				(errors ??= new List<Exception>()).AddRange(ex.InnerExceptions);
			}
		}
		if (errors is not null)
			throw new AggregateException("disposing reclaimed asset versions failed", errors);
	}

	// disposal exceptions don't stop the rest from being reclaimed
	private static void reclaimAll(List<AssetVersion> versions) {
		List<Exception>? errors = null;
		foreach (AssetVersion v in versions) {
			try {
				v.Reclaim();
			} catch (Exception ex) {
				(errors ??= new List<Exception>()).Add(ex);
			}
		}
		if (errors is not null)
			throw new AggregateException("disposing reclaimed asset versions failed", errors);
	}

	/// <summary>
	/// Reclaims every version this store holds, including pinned ones, discards prepared reloads, and
	/// disposes the registered dependency watchers.
	/// </summary>
	/// <remarks>
	/// Pins and borrows of this store's versions are invalid afterwards, since their values may be
	/// disposed. Loads still in progress finish without publishing.
	/// </remarks>
	/// <exception cref="AggregateException">
	/// Thrown if disposing versions or watchers threw; everything else is still disposed.
	/// </exception>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		// derived versions can reference the versions they were computed from, so deeper ones go first
		List<AssetVersion> reclaim = new();
		foreach (IDerivedSlot slot in derivedSlots.Keys)
			if (slot.DetachForStoreDispose() is AssetVersion v)
				reclaim.Add(v);
		derivedSlots.Clear();
		foreach (IAssetSlot slot in slots.Values)
			if (slot.DetachForStoreDispose() is AssetVersion v)
				reclaim.Add(v);
		lock (retiredLock) {
			foreach (Retired r in retired)
				reclaim.Add(r.Version);
			retired.Clear();
		}
		reclaim.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));
		List<IUntypedAssetDepWatcher> toDispose = new();
		lock (depLock) {
			foreach (UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher> reg in watchers.Values)
				toDispose.AddRange(reg.ReadSnapshot());
			watchers = new Dictionary<Type, UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>>();
			slotsByDep.Clear();
		}
		List<Exception>? errors = null;
		try {
			reclaimAll(reclaim);
		} catch (AggregateException ex) {
			(errors ??= new List<Exception>()).AddRange(ex.InnerExceptions);
		}
		foreach (IUntypedAssetDepWatcher w in toDispose) {
			try {
				w.Dispose();
			} catch (Exception ex) {
				(errors ??= new List<Exception>()).Add(ex);
			}
		}
		if (errors is not null)
			throw new AggregateException("disposing the asset store failed", errors);
	}

	internal void UnregisterRegistration(ulong registrationStoreId, AssetRegistrationKind kind, ulong id, Type? type) {
		if (id == 0)
			throw new InternalStateException("registration ID 0 is not valid");
		if (registrationStoreId != StoreId)
			throw new InternalStateException("UnregisterRegistration() called on foreign handle");
		switch (kind) {
		case AssetRegistrationKind.Source:
			if (type is not null)
				throw new InternalStateException("nonnull type passed for Source unregistry");
			lock (registryLock)
				_ = sources.UnregisterLocked(id, out _); // no-op if it's already unregistered
			break;
		case AssetRegistrationKind.Creator:
			if (type is null)
				throw new InternalStateException("null type passed for Creator unregistry");
			lock (registryLock) {
				if (creators.TryGetValue(type, out UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>? reg)) // no-op on missing
					_ = reg.UnregisterLocked(id, out _); // no-op if it's already unregistered
			}
			break;
		case AssetRegistrationKind.DepWatcher:
			if (type is null)
				throw new InternalStateException("null type passed for DepWatcher unregistry");
			lock (depLock) {
				if (watchers.TryGetValue(type, out UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>? reg)) { // no-op on missing
					_ = reg.UnregisterLocked(id, out OwnerOrderedEntry<IUntypedAssetDepWatcher>? ent); // no-op if it's already unregistered
					ent?.Item.Dispose();
				}
			}
			break;
		default:
			throw InternalStateException.BadOpenEnum(kind);
		}
	}

	// ==========================================================================
	// asset creation

	// the asset's own data, opened through the sources on first use and shared by the creators
	// tried for it; its deps belong to the asset whichever creator succeeds
	private sealed class MainStream(AssetStore store, AssetId id, Type type, DepCollector coll) : IDisposable {
		private Stream? stream;

		public async ValueTask<Stream> OpenAsync(CancellationToken ct) {
			stream ??= await store.tryAllSourcesAsync(id, coll, type, ct).ConfigureAwait(false);
			stream.Position = 0;
			return new SharedReadStream(stream);
		}

		public void Dispose() => stream?.Dispose();
	}

	// a read-only view of the main stream whose Dispose does nothing, so a creator disposing it
	// doesn't break the creators tried after it
	private sealed class SharedReadStream(Stream inner) : Stream {
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => inner.Length;
		public override long Position {
			get => inner.Position;
			set => inner.Position = value;
		}
		public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
		public override int Read(Span<byte> buffer) => inner.Read(buffer);
		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
		public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
		public override void Flush() {}
		public override void SetLength(long value) => throw new NotSupportedException("the asset's data stream is read-only");
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("the asset's data stream is read-only");
	}

	// pipeline components throw whatever they like; callers get an AssetLoadException with that as
	// the inner exception
	private static bool shouldWrap(Exception ex) =>
		ex is not (AssetLoadException or OperationCanceledException) && !ExceptionPolicy.IsInternalState(ex);

	private static AssetLoadException wrap(AssetId id, Type type, string what, Exception ex) =>
		new(id, type, $"{what} failed: {ex.GetType().Name}: {ex.Message}", ex);

	private async ValueTask<(IPendingAssetValue Prepared, ImmutableArray<IAssetDep> Deps)>
		tryPrepareValueAsync<T>(AssetId id, CancellationToken ct = default) where T : class {
		DepCollector rootColl = new();
		using MainStream main = new(this, id, typeof(T), rootColl);
		try {
			ImmutableDictionary<Type, UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>> dictsnap = Volatile.Read(ref creators);
			if (dictsnap.TryGetValue(typeof(T), out UnsafeOwnerOrderedRegistry<IUntypedAssetCreator>? reg)) {
				foreach (IUntypedAssetCreator creator in reg.ReadSnapshot()) {
					DepCollector childColl = new();
					AssetCreateInfo info = new(
						id,
						main.OpenAsync,
						(toFetch, passedCt) => tryAllSourcesAsync(toFetch, childColl, typeof(T), passedCt),
						(toFetch, passedCt) => tryAllSourcesAsyncOrNull(toFetch, childColl, typeof(T), passedCt)
					);
					UntypedCreateResult res = await creator.TryCreateAsync(info, childColl, ct).ConfigureAwait(false);
					switch (res.Kind.Tag) {
					case AssetCreateResultKind.Case.NotHandled:
						continue;
					case AssetCreateResultKind.Case.Success:
						if (res.Value is null)
							throw new InternalStateException("untyped asset creator prepare returned Success but didn't set Value");
						rootColl.MergeFrom(childColl);
						return (res.Value, rootColl.Snapshot());
					default:
						throw new UnreachableException();
					}
				}
			}
		} catch (Exception ex) when (shouldWrap(ex)) {
			throw wrap(id, typeof(T), "creating the asset", ex);
		}
		throw new AssetUnhandledException(id, typeof(T), "no registered asset creator accepted the asset");
	}

	private static T finalizePrepared<T>(AssetId id, IPendingAssetValue pv) where T : class {
		object v;
		try {
			v = pv.FinalizeValue(id);
		} catch (Exception ex) when (shouldWrap(ex)) {
			throw wrap(id, typeof(T), "finalizing the asset", ex);
		}
		if (v is not T typed)
			throw new InternalStateException($"pending asset value returned some value of type {v.GetType().FullName}, expected the {typeof(T).FullName} from the creator");
		// a derived type can add IDisposable even when T doesn't have it
		if (v is IDisposable d && v is not IAssetStoreDisposable) {
			d.Dispose();
			throw new AssetLoadException(id, typeof(T), $"the created value's type {v.GetType().FullName} implements IDisposable but not IAssetStoreDisposable");
		}
		return typed;
	}

	// ==========================================================================
	// the rest of the asset pipeline
	private static async ValueTask<Stream> fixstream(Stream stream, CancellationToken ct) {
		if (stream.CanSeek)
			return stream;
		MemoryStream ms = new();
		try {
			await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
			ms.Position = 0;
			return ms;
		} catch {
			ms.Dispose();
			throw;
		} finally {
			stream.Dispose();
		}
	}

	private async ValueTask<Stream?> tryAllSourcesAsyncOrNull(AssetId id, DepCollector parentColl, Type t, CancellationToken ct) {
		IReadOnlyList<IAssetSource> snapshot = sources.ReadSnapshot();
		AssetSourceInfo info = new(id);
		foreach (IAssetSource source in snapshot) {
			DepCollector childColl = new();
			AssetSourceResult res = await source.TrySourceAsync(info, childColl, ct).ConfigureAwait(false);
			switch (res.Kind.Tag) {
			case AssetSourceResultKind.Case.NotHandled:
				continue;
			case AssetSourceResultKind.Case.Success:
				if (res.Stream is null)
					throw new AssetLoadException(id, t, "asset source returned Success but didn't set Stream");
				parentColl.MergeFrom(childColl);
				return await fixstream(res.Stream, ct).ConfigureAwait(false);
			default:
				throw new UnreachableException();
			}
		}
		return null;
	}

	private async ValueTask<Stream> tryAllSourcesAsync(AssetId id, DepCollector parentColl, Type t, CancellationToken ct) =>
		await tryAllSourcesAsyncOrNull(id, parentColl, t, ct) ?? throw new AssetUnhandledException(id, t, "no registered asset source managed to provide the asset");

	// ==========================================================================
	// dependencies
	private void onDepChanged(IAssetDep dep) {
		IAssetSlot[] affected;
		lock (depLock) {
			if (!slotsByDep.TryGetValue(dep, out HashSet<IAssetSlot>? slots))
				return;
			affected = slots.ToArray();
		}
		foreach (IAssetSlot slot in affected)
			slot.QueueReloadFromDep(dep);
	}

	private void onSlotPublished(IAssetSlot slot, ImmutableArray<IAssetDep> oldDeps, ImmutableArray<IAssetDep> newDeps) {
		static HashSet<IAssetDep> makeSet(ImmutableArray<IAssetDep> deps) {
			HashSet<IAssetDep> s = new();
			for (int i = 0; i < deps.Length; i++)
				s.Add(deps[i]);
			return s;
		}

		// note: do the watch/unwatch calls under the lock, because the alternative is a race window where
		// where the map says the dep is watched but it actually isn't, so an update would be missed if it
		// happened right then
		lock (depLock) {
			HashSet<IAssetDep> oldSet = makeSet(oldDeps);
			HashSet<IAssetDep> newSet = makeSet(newDeps);
			foreach (IAssetDep dep in oldSet) {
				if (newSet.Contains(dep))
					continue;
				if (!slotsByDep.TryGetValue(dep, out HashSet<IAssetSlot>? users))
					continue;
				users.Remove(slot);
				if (users.Count != 0)
					continue;
				slotsByDep.Remove(dep);
				if (watchers.TryGetValue(dep.GetType(), out UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>? reg)) {
					IReadOnlyList<IUntypedAssetDepWatcher> snapshot = reg.ReadSnapshot();
					for (int i = snapshot.Count - 1; i >= 0; i--) {
						IUntypedAssetDepWatcher watcher = snapshot[i];
						watcher.Unwatch(dep);
					}
				}
			}
			foreach (IAssetDep dep in newSet) {
				if (oldSet.Contains(dep))
					continue;
				if (!slotsByDep.TryGetValue(dep, out HashSet<IAssetSlot>? users)) {
					users = new HashSet<IAssetSlot>();
					slotsByDep.Add(dep, users);
					if (watchers.TryGetValue(dep.GetType(), out UnsafeOwnerOrderedRegistry<IUntypedAssetDepWatcher>? reg)) {
						IReadOnlyList<IUntypedAssetDepWatcher> snapshot = reg.ReadSnapshot();
						foreach (IUntypedAssetDepWatcher watcher in snapshot)
							watcher.Watch(dep);
					}
				}
				users.Add(slot);
			}
		}
	}
}
