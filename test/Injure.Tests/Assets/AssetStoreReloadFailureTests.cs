// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;
using Injure.Mods;

namespace Injure.Tests.Assets;

public sealed class AssetStoreReloadFailureTests {
	private const string ownerId = "test";

	[Fact]
	public static async Task ExplicitPrepareFailureThrowsAndRecordsFailure() {
		AssetStore store = new();
		ControllableCreator creator = new();
		InvalidOperationException ex = new("prepare failed");
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		ulong oldver = asset.Borrow().Version;

		creator.PrepareException = ex;
		ForeignException fex = await Assert.ThrowsAsync<ForeignException>(() =>
			asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken)
		);
		Assert.Equal(ex.GetType().FullName, fex.OriginalFullTypeName);
		Assert.Equal(ex.Message, fex.OriginalMessage);
		Assert.False(asset.HasQueuedReload);
		Assert.Equal(oldver, asset.Borrow().Version);

		AssetReloadFailure failure = Assert.IsType<AssetReloadFailure>(asset.LastReloadFailure);
		Assert.Equal(2ul, failure.TargetVersion);
		Assert.Equal(AssetReloadFailureStage.Prepare, failure.Stage);
		Assert.Equal(AssetReloadRequestOrigin.Explicit, failure.Origin);
		Assert.Null(failure.Trigger);
		Assert.Equal(ex.GetType().FullName, failure.ExceptionSnapshot.FullTypeName);
		Assert.Equal(ex.Message, failure.ExceptionSnapshot.Message);

		AssetReloadFailure logged = Assert.Single(store.DrainReloadFailures());
		Assert.Equal(failure, logged);
	}

	[Fact]
	public static async Task WatcherPrepareFailureIsRecordedButDoesntThrowIntoRaise() {
		AssetStore store = new();
		ControllableCreator creator = new();
		TestDependency dep = new("somedep");
		TestDependencyWatcher watcher = new();
		InvalidOperationException ex = new("dependency reload failed");
		store.RegisterSource(ownerId, new TestSource(dep), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");
		store.RegisterDependencyWatcher(ownerId, watcher, "watcher");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		creator.PrepareException = ex;

		watcher.Raise(dep);
		await AssetTestWait.ForReloadFailureAsync(asset);

		Assert.False(asset.HasQueuedReload);
		Assert.Equal(1ul, asset.Borrow().Version);

		AssetReloadFailure failure = Assert.IsType<AssetReloadFailure>(asset.LastReloadFailure);
		Assert.Equal(2ul, failure.TargetVersion);
		Assert.Equal(AssetReloadFailureStage.Prepare, failure.Stage);
		Assert.Equal(AssetReloadRequestOrigin.Dependency, failure.Origin);
		Assert.True(failure.Trigger is not null);
		Assert.Equal(dep.GetType().FullName, failure.Trigger.Value.FullTypeName);
		Assert.Equal(dep.DebugDescription, failure.Trigger.Value.DebugDescription);
		Assert.Equal(ex.GetType().FullName, failure.ExceptionSnapshot.FullTypeName);
		Assert.Equal(ex.Message, failure.ExceptionSnapshot.Message);

		AssetReloadFailure logged = Assert.Single(store.DrainReloadFailures());
		Assert.Equal(failure, logged);
	}

	[Fact]
	public static async Task FinalizeFailureIsReportedAndKeepsOldVersionLive() {
		AssetStore store = new();
		ControllableCreator creator = new();
		InvalidOperationException ex = new("finalize failed");
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		TestAsset oldValue = asset.Borrow().Value;
		creator.FinalizeException = ex;

		await asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		Assert.True(asset.HasQueuedReload);

		AssetReloadReport report = store.ApplyQueuedReloads();
		Assert.Equal(0, report.AppliedCount);
		AssetReloadFailure failure = Assert.Single(report.Failures);
		Assert.Equal(2ul, failure.TargetVersion);
		Assert.Equal(AssetReloadFailureStage.Finalize, failure.Stage);
		Assert.Equal(AssetReloadRequestOrigin.Explicit, failure.Origin);
		Assert.Equal(ex.GetType().FullName, failure.ExceptionSnapshot.FullTypeName);
		Assert.Equal(ex.Message, failure.ExceptionSnapshot.Message);
		Assert.False(asset.HasQueuedReload);
		Assert.Same(oldValue, asset.Borrow().Value);
		Assert.True(asset.LastReloadFailure is not null);
		Assert.Equal(ex.GetType().FullName, asset.LastReloadFailure.ExceptionSnapshot.FullTypeName);
		Assert.Equal(ex.Message, asset.LastReloadFailure.ExceptionSnapshot.Message);
		Assert.Equal(2, creator.PreparedDisposeCalls);

		AssetReloadFailure logged = Assert.Single(store.DrainReloadFailures());
		Assert.Equal(failure, logged);
	}

	[Fact]
	public static async Task ApplyQueuedReloadsOrThrowThrowsOnFinalizeFailure() {
		AssetStore store = new();
		ControllableCreator creator = new();
		InvalidOperationException ex = new("finalize failed");
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		creator.FinalizeException = ex;
		await asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

		AggregateException aggregate = Assert.Throws<AggregateException>(() => store.ApplyQueuedReloadsOrThrow());
		Assert.Single(aggregate.InnerExceptions);
		Assert.IsType<ForeignException>(aggregate.InnerExceptions[0]);
		var fex = (ForeignException)aggregate.InnerExceptions[0];
		Assert.Equal(ex.GetType().FullName, fex.OriginalFullTypeName);
		Assert.Equal(ex.Message, fex.OriginalMessage);
	}

	[Fact]
	public static async Task SuccessfulReloadAfterFailureClearsLastReloadFailure() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

		creator.PrepareException = new InvalidOperationException("prepare failed");
		ForeignException fex = await Assert.ThrowsAsync<ForeignException>(() =>
			asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken)
		);
		Assert.Equal(typeof(InvalidOperationException).FullName, fex.OriginalFullTypeName);
		Assert.NotNull(asset.LastReloadFailure);

		creator.PrepareException = null;
		creator.OverrideValue = "recovered";
		await asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		Assert.Equal(1, store.ApplyQueuedReloadsOrThrow());

		AssetLease<TestAsset> lease = asset.Borrow();
		Assert.Equal(3ul, lease.Version);
		Assert.Equal("recovered", lease.Value.Val);
		Assert.Null(asset.LastReloadFailure);
	}
}
