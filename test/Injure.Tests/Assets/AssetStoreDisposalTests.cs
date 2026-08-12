// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreDisposalTests {
	private const string ownerId = "test";

	[Fact]
	public async Task PreparedDataIsDisposedAfterInitialMaterialize() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

		Assert.Equal(1, creator.PreparedDisposeCalls);
	}

	[Fact]
	public async Task PreparedDataIsDisposedAfterSuccessfulReload() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		await asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		store.ApplyQueuedReloadsOrThrow();

		Assert.Equal(2, creator.PreparedDisposeCalls);
	}

	[Fact]
	public async Task PreparedDataIsDisposedWhenFinalizeFails() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		creator.FinalizeException = new InvalidOperationException("finalize failed");
		await asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		store.ApplyQueuedReloads();

		Assert.Equal(2, creator.PreparedDisposeCalls);
	}

	[Fact]
	public async Task SupersededPendingReloadDisposesPreparedData() {
		AssetStore store = new();
		ControllableCreator creator = new();
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, creator, "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		await asset.WarmAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		await asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		Assert.True(asset.HasQueuedReload);

		await asset.QueueReloadAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
		Assert.True(asset.HasQueuedReload);
		store.ApplyQueuedReloadsOrThrow();

		Assert.Equal(3, creator.PreparedDisposeCalls);
		Assert.Equal(3ul, asset.Borrow().Version);
	}
}
