// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;
using Injure.Mods;

namespace Injure.Tests.Assets;

public static class AssetStoreTests {
	[Fact]
	public static void HandlesAreCanonicalAndBorrowsLoad() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "hello");
		Assert.Same(a, ts.Store.GetAsset<PlainValue>(TestStore.Id("a.txt")));
		Assert.False(a.IsLoaded);
		AssetBorrow<PlainValue> b = a.Borrow();
		Assert.Equal("hello", b.Value.Text);
		Assert.Equal(1ul, b.Version);
		Assert.Equal([new MemoryDep(TestStore.Id("a.txt"))], b.Deps.ToArray());
		Assert.True(a.IsLoaded);
	}

	[Fact]
	public static void BorrowingRequiresAnAttachedThreadButPinningDoesnt() {
		using TestStore ts = new();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "hello");
		Assert.Throws<InvalidOperationException>(() => a.Borrow());
		Assert.Throws<InvalidOperationException>(() => a.TryPassiveBorrow(out _));
		using AssetPin<PlainValue> pin = a.Pin();
		Assert.Equal("hello", pin.Value.Text);
	}

	[Fact]
	public static void DefaultValueBorrowIsInvalid() {
		bool threw = false;
		try {
			_ = default(AssetBorrow<PlainValue>).Value;
		} catch (InvalidDefaultException) {
			threw = true;
		}
		Assert.True(threw);
	}

	[Fact]
	public static void BorrowsThrowAfterTheirThreadsBoundary() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "hello");
		AssetBorrow<PlainValue> b = a.Borrow();
		_ = b.Value;
		att.AtReloadBoundary();
		bool threw = false;
		try {
			_ = b.Value;
		} catch (InvalidOperationException) {
			threw = true;
		}
		Assert.True(threw);
	}

	[Fact]
	public static void ReplacedVersionsAreReclaimedAfterEveryBoundary() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<ResourceValue> a = ts.Get<ResourceValue>("a.txt", "v1");
		ResourceValue v1 = a.Borrow().Value;
		Assert.True(v1.IsAssetStoreOwned);

		ts.Reload(a, "v2");
		AssetBorrow<ResourceValue> b2 = a.Borrow();
		Assert.Equal("v2", b2.Value.Text);
		Assert.Equal(2ul, b2.Version);
		// this thread hasn't passed a boundary since the reload, so v1 stays alive
		Assert.False(v1.Disposed);
		att.AtReloadBoundary();
		Assert.False(v1.Disposed);
		ts.Store.ApplyQueuedReloads();
		Assert.True(v1.Disposed);
		Assert.False(v1.IsAssetStoreOwned);
		Assert.Throws<ObjectDisposedException>(() => v1.Text);
	}

	[Fact]
	public static void PinsKeepReplacedVersionsAlive() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<ResourceValue> a = ts.Get<ResourceValue>("a.txt", "v1");
		AssetPin<ResourceValue> pin = a.Borrow().Pin();
		Assert.True(pin.IsCurrent);

		ts.Reload(a, "v2");
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
		Assert.False(pin.IsCurrent);
		Assert.Equal("v1", pin.Value.Text);
		Assert.Equal(1ul, pin.Version);

		ResourceValue v1 = pin.Value;
		pin.Dispose();
		pin.Dispose();
		Assert.Throws<ObjectDisposedException>(() => pin.Value);
		// releasing the last pin doesn't reclaim by itself
		Assert.False(v1.Disposed);
		ts.Store.ApplyQueuedReloads();
		Assert.True(v1.Disposed);
	}

	[Fact]
	public static void PinsWorkFromUnattachedThreads() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<ResourceValue> a = ts.Get<ResourceValue>("a.txt", "v1");
		AssetPin<ResourceValue>? pin = null;
		ThreadUtil.OnNewThread(() => pin = a.Pin());
		ts.Reload(a, "v2");
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
		Assert.Equal("v1", pin!.Value.Text);
		ThreadUtil.OnNewThread(() => {
			Assert.True(a.TryPassivePin(out AssetPin<ResourceValue>? current));
			Assert.Equal("v2", current.Value.Text);
			current.Dispose();
			pin.Dispose();
		});
		ts.Store.ApplyQueuedReloads();
		Assert.Throws<ObjectDisposedException>(() => pin.Value);
	}

	[Fact]
	public static void ReclamationWaitsForEveryAttachedThreadAndRunsOnTheApplyingOne() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<ResourceValue> a = ts.Get<ResourceValue>("a.txt", "v1");
		ResourceValue v1 = a.Borrow().Value;

		using ManualResetEventSlim attached = new(), doBoundary = new(), boundaryDone = new(), exit = new();
		Exception? workerError = null;
		Thread worker = new(() => {
			try {
				using AssetThreadAttachment w = ts.Store.AttachCurrentThread();
				attached.Set();
				doBoundary.Wait();
				w.AtReloadBoundary();
				boundaryDone.Set();
				exit.Wait();
			} catch (Exception ex) {
				workerError = ex;
				attached.Set();
				boundaryDone.Set();
			}
		});
		worker.Start();
		attached.Wait(TestContext.Current.CancellationToken);

		ts.Reload(a, "v2");
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
		// the worker hasn't passed a boundary since the reload
		Assert.False(v1.Disposed);

		doBoundary.Set();
		boundaryDone.Wait(TestContext.Current.CancellationToken);
		// reporting a boundary doesn't reclaim anything by itself
		Assert.False(v1.Disposed);
		ts.Store.ApplyQueuedReloads();
		Assert.True(v1.Disposed);
		Assert.Equal(Environment.CurrentManagedThreadId, v1.DisposeThreadId);

		exit.Set();
		worker.Join();
		Assert.Null(workerError);
	}

	[Fact]
	public static void StoreOwnedValuesCantBeDisposedByOthers() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<ResourceValue> a = ts.Get<ResourceValue>("a.txt", "v1");
		ResourceValue v = a.Borrow().Value;
		Assert.True(v.IsAssetStoreOwned);
		Assert.Equal(TestStore.Id("a.txt"), v.Custody!.AssetId);
		Assert.Equal(1ul, v.Custody.Version);
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(v.Dispose);
		Assert.Contains("test::a.txt", ex.Message);
		Assert.False(v.Disposed);
	}

	private sealed class BareDisposable : IDisposable {
		public void Dispose() {}
	}

	private class PlainBase {}

	private sealed class DisposableDerived : PlainBase, IDisposable {
		public bool Disposed { get; private set; }
		public void Dispose() => Disposed = true;
	}

	[Fact]
	public static void DisposablesWithoutTheStoreInterfaceAreRejected() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Assert.Throws<ArgumentException>(() => ts.Register(static _ => new BareDisposable()));

		// a subclass can add IDisposable to a type that doesn't have it
		DisposableDerived? made = null;
		_ = ts.Register<PlainBase>(_ => made = new DisposableDerived());
		Asset<PlainBase> a = ts.Get<PlainBase>("a.txt", "x");
		Assert.Throws<AssetLoadException>(() => a.Borrow());
		Assert.True(made!.Disposed);
	}

	[Fact]
	public static void DisposalFreeValuesStayUsableThroughPins() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "v1");
		using AssetPin<PlainValue> pin = a.Pin();
		ts.Reload(a, "v2");
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
		Assert.Equal("v2", a.Borrow().Value.Text);
		Assert.Equal("v1", pin.Value.Text);
	}

	[Fact]
	public static void FailedReloadsKeepTheCurrentVersion() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "v1");
		_ = a.Borrow();
		ts.Source.Set(a.AssetId, "fail");
		AssetLoadException ex = Assert.Throws<AssetLoadException>(() => a.QueueReload(TestContext.Current.CancellationToken));
		Assert.IsType<InvalidDataException>(ex.InnerException);
		AssetReloadFailure? f = a.LastReloadFailure;
		Assert.NotNull(f);
		Assert.Equal(AssetReloadFailureStage.Prepare, f.Stage);
		Assert.Equal(AssetReloadRequestOrigin.Explicit, f.Origin);
		Assert.Equal(0, ts.Store.ApplyQueuedReloads().AppliedCount);
		Assert.Equal("v1", a.Borrow().Value.Text);
		Assert.Single(ts.Store.DrainReloadFailures());

		ts.Reload(a, "v3");
		Assert.Null(a.LastReloadFailure);
		Assert.Equal("v3", a.Borrow().Value.Text);
	}

	[Fact]
	public static void ReloadingNeedsACurrentVersion() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "v1");
		Assert.Throws<InvalidOperationException>(() => a.QueueReload(TestContext.Current.CancellationToken));
		Assert.False(a.IsLoaded);
		a.Warm(TestContext.Current.CancellationToken);
		ts.Reload(a, "v2");
		Assert.Equal("v2", a.Borrow().Value.Text);
	}

	[Fact]
	public static void FailedInitialLoadsAreReported() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "fail");
		AssetLoadException ex = Assert.Throws<AssetLoadException>(() => a.Borrow());
		Assert.IsType<InvalidDataException>(ex.InnerException);
		Assert.Equal(a.AssetId, ex.AssetId);
		Assert.Equal(typeof(PlainValue), ex.AssetType);
		Assert.NotNull(a.LastLoadException);
		Asset<PlainValue> missing = ts.Store.GetAsset<PlainValue>(TestStore.Id("missing.txt"));
		Assert.Throws<AssetUnhandledException>(() => missing.Borrow());
	}

	private sealed class ManualDepWatcher : IAssetDepWatcher<MemoryDep> {
		public HashSet<MemoryDep> Watched { get; } = new();
		public bool Disposed { get; private set; }
		public event Action<MemoryDep>? Changed;
		public void Watch(MemoryDep dep) => Watched.Add(dep);
		public void Unwatch(MemoryDep dep) => Watched.Remove(dep);
		public void Raise(MemoryDep dep) => Changed?.Invoke(dep);
		public void Dispose() => Disposed = true;
	}

	[Fact]
	public static void DepWatchersQueueReloads() {
		ManualDepWatcher watcher = new();
		using (TestStore ts = new()) {
			_ = ts.Store.RegisterDepWatcher(new OwnerOrderedEntry<IAssetDepWatcher<MemoryDep>>(watcher, TestStore.Ns, "watcher"));
			using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
			Asset<PlainValue> a = ts.Get<PlainValue>("a.txt", "v1");
			_ = a.Borrow();
			MemoryDep dep = new(a.AssetId);
			Assert.Contains(dep, watcher.Watched);

			ts.Source.Set(a.AssetId, "v2");
			watcher.Raise(dep);
			Assert.True(SpinWait.SpinUntil(() => a.HasQueuedReload, TimeSpan.FromSeconds(10)));
			Assert.Equal(1, ts.Store.ApplyQueuedReloads().AppliedCount);
			Assert.Equal("v2", a.Borrow().Value.Text);
		}
		Assert.True(watcher.Disposed);
	}

	private sealed class Named(string text) {
		public string Text { get; } = text;
	}

	[Fact]
	public static void CreatorsShareTheAssetsDataStream() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		int opens = 0;
		_ = ts.Register("partial-reader", async info => {
			Stream s = await info.OpenAsync(default);
			opens++;
			s.ReadByte();
			s.ReadByte();
			// disposing it must not break the creators after this one
			await s.DisposeAsync();
			return AssetCreateResult<Named>.NotHandled();
		});
		_ = ts.Register("whole-reader", async info => {
			Stream s = await info.OpenAsync(default);
			opens++;
			Assert.Equal(0, s.Position);
			Assert.False(s.CanWrite);
			using StreamReader sr = new(s);
			return AssetCreateResult<Named>.Success(new Named(await sr.ReadToEndAsync()));
		});
		Asset<Named> a = ts.Get<Named>("a.txt", "hello");
		Assert.Equal("hello", a.Borrow().Value.Text);
		Assert.Equal(2, opens);
		Assert.Equal([new MemoryDep(a.AssetId)], a.Borrow().Deps.ToArray());
	}

	[Fact]
	public static void CreatorsCanSkipTheDataAndFetchOtherAssets() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		ts.Source.Set(TestStore.Id("other.txt"), "other");
		// fetches a dependency, then declines: its deps must not end up on the asset
		_ = ts.Register("decliner", async info => {
			await using Stream s = await info.FetchAsync(TestStore.Id("other.txt"), default);
			Assert.Null(await info.TryFetchAsync(TestStore.Id("nope.txt"), default));
			return AssetCreateResult<Named>.NotHandled();
		});
		// never opens its own data, which no source has
		_ = ts.Register("procedural", static info => ValueTask.FromResult(AssetCreateResult<Named>.Success(new Named(info.AssetId.Path))));
		Asset<Named> a = ts.Store.GetAsset<Named>(TestStore.Id("generated"));
		AssetBorrow<Named> b = a.Borrow();
		Assert.Equal("generated", b.Value.Text);
		Assert.True(b.Deps.IsEmpty);
	}

	[Fact]
	public static void EvictRetiresTheCurrentVersion() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<ResourceValue> a = ts.Get<ResourceValue>("a.txt", "v1");
		ResourceValue v1 = a.Borrow().Value;
		Assert.True(a.Evict());
		Assert.False(a.IsLoaded);
		Assert.False(a.Evict());
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
		Assert.True(v1.Disposed);
		AssetBorrow<ResourceValue> b = a.Borrow();
		Assert.Equal("v1", b.Value.Text);
		Assert.True(b.Version > 1);
	}

	[Fact]
	public static void DisposingTheStoreReclaimsEverything() {
		TestStore ts = new();
		ResourceValue current, pinned;
		AssetPin<ResourceValue> pin;
		Asset<ResourceValue> a;
		using (AssetThreadAttachment att = ts.Store.AttachCurrentThread()) {
			a = ts.Get<ResourceValue>("a.txt", "v1");
			pin = a.Pin();
			pinned = pin.Value;
			ts.Reload(a, "v2");
			current = a.Borrow().Value;
		}
		ts.Store.Dispose();
		ts.Store.Dispose();
		Assert.True(current.Disposed);
		Assert.True(pinned.Disposed);
		Assert.Throws<ObjectDisposedException>(() => ts.Store.GetAsset<PlainValue>(TestStore.Id("a.txt")));
		Assert.Throws<ObjectDisposedException>(() => a.Pin());
		Assert.Throws<ObjectDisposedException>(() => ts.Store.ApplyQueuedReloads());
		pin.Dispose();
	}

	[Fact]
	public static void AttachmentsAreBoundToTheirThread() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Assert.Throws<InvalidOperationException>(ts.Store.AttachCurrentThread);
		AggregateException ex = Assert.Throws<AggregateException>(() => ThreadUtil.OnNewThread(att.AtReloadBoundary));
		Assert.IsType<InvalidOperationException>(ex.InnerException);
	}
}
