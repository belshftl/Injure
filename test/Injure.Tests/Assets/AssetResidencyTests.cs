// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetResidencyTests {
	// a hand-written group, as a game would write for a scene
	private sealed class SceneGroup(TestStore ts) : AssetGroup {
		public Asset<ResourceValue> Hero { get; } = ts.Get<ResourceValue>("hero.txt", "hero");
		public Asset<ResourceValue> Tree { get; } = ts.Get<ResourceValue>("tree.txt", "tree");
		public override IReadOnlyList<IUntypedAsset> Members => [Hero, Tree];
	}

	private static void boundary(TestStore ts, AssetThreadAttachment att) {
		att.AtReloadBoundary();
		ts.Store.ApplyQueuedReloads();
	}

	[Fact]
	public static void ResidenciesLoadTheirGroupAndGiveTypedAccess() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		SceneGroup scene = new(ts);
		List<float> progress = new();
		using AssetResidency<SceneGroup> r = scene.MakeResident(new SyncProgress(progress.Add), ct: TestContext.Current.CancellationToken);
		Assert.Same(scene, r.Group);
		Assert.True(r.Group.Hero.IsLoaded);
		Assert.True(r.Group.Tree.IsLoaded);
		Assert.Equal(1f, progress.Max());
		Assert.Equal("hero", r.Group.Hero.Borrow().Value.Text);
	}

	[Fact]
	public static void TheLastReleaseEvictsAtTheNextApply() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		SceneGroup scene = new(ts);
		AssetResidency<SceneGroup> a = scene.MakeResident(ct: TestContext.Current.CancellationToken);
		AssetResidency b = AssetSet.Of(scene).MakeResident(ct: TestContext.Current.CancellationToken);
		ResourceValue hero = scene.Hero.Borrow().Value;

		a.Dispose();
		a.Dispose();
		Assert.True(a.IsDisposed);
		boundary(ts, att);
		// b still includes them
		Assert.True(scene.Hero.IsLoaded);

		b.Dispose();
		Assert.True(scene.Hero.IsLoaded);
		ts.Store.ApplyQueuedReloads();
		Assert.False(scene.Hero.IsLoaded);
		Assert.False(scene.Tree.IsLoaded);
		// evicted versions are retired as usual: reclaimed once this thread passes a boundary
		Assert.False(hero.Disposed);
		boundary(ts, att);
		Assert.True(hero.Disposed);
	}

	[Fact]
	public static void NeverResidentAssetsStayLoaded() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> lazy = ts.Get<PlainValue>("lazy.txt", "x");
		_ = lazy.Borrow();
		new SceneGroup(ts).MakeResident(ct: TestContext.Current.CancellationToken).Dispose();
		boundary(ts, att);
		Assert.True(lazy.IsLoaded);
	}

	private sealed class SyncProgress(Action<float> report) : IProgress<float> {
		public void Report(float value) {
			lock (this)
				report(value);
		}
	}

	[Fact]
	public static void NonResidentLoadsAreReported() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		List<AssetKey> reported = new();
		ts.Store.NonResidentLoad += reported.Add;
		SceneGroup scene = new(ts);
		using (scene.MakeResident(ct: TestContext.Current.CancellationToken)) {
			_ = scene.Hero.Borrow();
			Assert.Empty(reported);
		}
		Asset<PlainValue> lazy = ts.Get<PlainValue>("lazy.txt", "x");
		_ = lazy.Borrow();
		_ = lazy.Borrow();
		using (ts.Get<PlainValue>("pinned.txt", "y").Pin()) {
		}
		Assert.Equal([new AssetKey(lazy.AssetId, typeof(PlainValue)), new AssetKey(TestStore.Id("pinned.txt"), typeof(PlainValue))], reported);
	}

	[Fact]
	public static void FailedResidenciesDontKeepAnything() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		Asset<PlainValue> good = ts.Get<PlainValue>("good.txt", "ok");
		Asset<PlainValue> bad = ts.Get<PlainValue>("bad.txt", "fail");
		AssetSet set = new([good, bad]);
		Assert.ThrowsAny<AssetLoadException>(() => set.MakeResident(maxConcurrency: 1, ct: TestContext.Current.CancellationToken));
		boundary(ts, att);
		Assert.False(good.IsLoaded);
	}

	[Fact]
	public static void ResidentAssetsStillReload() {
		using TestStore ts = new();
		using AssetThreadAttachment att = ts.Store.AttachCurrentThread();
		SceneGroup scene = new(ts);
		using AssetResidency<SceneGroup> r = scene.MakeResident(ct: TestContext.Current.CancellationToken);
		ResourceValue v1 = scene.Hero.Borrow().Value;
		ts.Reload(scene.Hero, "hero2");
		boundary(ts, att);
		// the residency keeps the asset loaded, not the old version alive
		Assert.True(v1.Disposed);
		Assert.Equal("hero2", r.Group.Hero.Borrow().Value.Text);
	}

	// only the blocking creator below handles this type
	private sealed class Slow(string text) {
		public string Text { get; } = text;
	}

	[Fact]
	public static async Task EvictionWaitsForLoadsInProgress() {
		using TestStore ts = new();
		// no attachment: this awaits, so it can resume on another thread, and it never borrows
		using ManualResetEventSlim release = new();
		ts.Register<Slow>("slow", async info => {
			using StreamReader sr = new(await info.OpenAsync(TestContext.Current.CancellationToken));
			string text = await sr.ReadToEndAsync(TestContext.Current.CancellationToken);
			if (text == "slow")
				release.Wait();
			return AssetCreateResult<Slow>.Success(new Slow(text));
		});
		Asset<Slow> a = ts.Get<Slow>("a.txt", "v1");
		AssetResidency r = new AssetSet([a]).MakeResident(ct: TestContext.Current.CancellationToken);
		ts.Source.Set(a.AssetId, "slow");
		Task reload = a.QueueReloadAsync(TestContext.Current.CancellationToken);
		r.Dispose();
		ts.Store.ApplyQueuedReloads();
		// the reload is still being prepared, so eviction has to wait
		Assert.True(a.IsLoaded);
		release.Set();
		await reload.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		ts.Store.ApplyQueuedReloads();
		Assert.False(a.IsLoaded);
	}
}
