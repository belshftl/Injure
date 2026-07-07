// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Internals.Tests.Assets;

public sealed class AssetStoreWatcherTests {
	private const string ownerId = "test";

	[Fact]
	public void WatchersRegisteredBeforeDependencyPublicationAllWatchIt() {
		AssetStore store = new();
		TestDependency dep = new("dep");
		TestDependencyWatcher watcherA = new();
		TestDependencyWatcher watcherB = new();
		store.RegisterSource(ownerId, new TestSource(dep), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");
		store.RegisterDependencyWatcher(ownerId, watcherA, "watcher-a");
		store.RegisterDependencyWatcher(ownerId, watcherB, "watcher-b");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		asset.Warm(TestContext.Current.CancellationToken);

		Assert.Equal(["watch:dep"], watcherA.Log);
		Assert.Equal(["watch:dep"], watcherB.Log);
		Assert.Contains(dep, watcherA.Watched);
		Assert.Contains(dep, watcherB.Watched);
	}

	[Fact]
	public void WatcherRegisteredAfterDependencyPublicationWatchesExistingDependency() {
		AssetStore store = new();
		TestDependency dep = new("dep");
		TestDependencyWatcher watcherA = new();
		TestDependencyWatcher watcherB = new();
		store.RegisterSource(ownerId, new TestSource(dep), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");
		store.RegisterDependencyWatcher(ownerId, watcherA, "watcher-a");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		asset.Warm(TestContext.Current.CancellationToken);

		store.RegisterDependencyWatcher(ownerId, watcherB, "watcher-b");

		Assert.Equal(["watch:dep"], watcherB.Log);
		Assert.Contains(dep, watcherB.Watched);
	}

	[Fact]
	public async Task SecondWatcherOfSameTypeCanTriggerReload() {
		AssetStore store = new();
		TestDependency dep = new("dep");
		TestDependencyWatcher watcherA = new();
		TestDependencyWatcher watcherB = new();
		store.RegisterSource(ownerId, new TestSource(dep), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");
		store.RegisterDependencyWatcher(ownerId, watcherA, "watcher-a");
		store.RegisterDependencyWatcher(ownerId, watcherB, "watcher-b");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken);
		watcherB.Raise(dep);
		await AssetTestWait.ForQueuedReloadAsync(asset);

		Assert.Equal(1, store.ApplyQueuedReloadsOrThrow());
		Assert.Equal(2ul, asset.Borrow().Version);
	}

	[Fact]
	public void DependencyReplacementUnwatchesOldDependencyAndWatchesNewDependency() {
		AssetStore store = new();
		TestDependency depA = new("dep-a");
		TestDependency depB = new("dep-b");
		TestDependencyWatcher watcher = new();
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterCreator(
			ownerId,
			new SteppingCreator(
				new Step("step-a", Handled: true, depA),
				new Step("step-b", Handled: true, depB)
			),
			"creator"
		);
		store.RegisterDependencyWatcher(ownerId, watcher, "watcher");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		asset.Warm(TestContext.Current.CancellationToken);
		asset.QueueReload(TestContext.Current.CancellationToken);
		store.ApplyQueuedReloadsOrThrow();

		Assert.Equal(["watch:dep-a", "unwatch:dep-a", "watch:dep-b"], watcher.Log);
		Assert.DoesNotContain(depA, watcher.Watched);
		Assert.Contains(depB, watcher.Watched);
	}

	[Fact]
	public void UnregisteredWatcherNoLongerTriggersReloads() {
		AssetStore store = new();
		TestDependency dep = new("dep");
		TestDependencyWatcher watcher = new();
		store.RegisterSource(ownerId, new TestSource(dep), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");
		AssetStoreRegistration r = store.RegisterDependencyWatcher(ownerId, watcher, "watcher");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		asset.Warm(TestContext.Current.CancellationToken);
		r.Remove();

		watcher.Raise(dep);
		Assert.False(asset.HasQueuedReload);
		Assert.True(watcher.Disposed);
	}
}
