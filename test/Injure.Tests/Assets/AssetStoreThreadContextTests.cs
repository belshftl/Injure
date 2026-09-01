// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreThreadContextTests {
	private const string ownerId = "test";

	[Fact]
	public static void SameThreadCanAttachToMultipleStores() {
		AssetStore a = new();
		AssetStore b = new();
		AssetStore c = new();
		using AssetThreadCtx ctxA = a.AttachCurrentThread();
		using AssetThreadCtx ctxB = b.AttachCurrentThread();
		using AssetThreadCtx ctxC = c.AttachCurrentThread();
		ctxA.AtSafeBoundary();
		ctxB.AtSafeBoundary();
		ctxC.AtSafeBoundary();
	}

	[Fact]
	public static void RetiredVerIsReclaimedOnlyAfterSafeBoundary() {
		AssetStore store = new();
		using AssetThreadCtx mainCtx = store.AttachCurrentThread();
		store.RegisterSource(new TestSource(), "source");
		store.RegisterResolver(new TestResolver(), "resolver");
		store.RegisterStagedCreator(new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		TestAsset v = asset.Borrow().Value;

		ThreadCheckpoint first = new();
		ThreadCheckpoint second = new();
		Exception? ex = null;
		Thread thread = new(() => {
				try {
					using AssetThreadCtx ctx = store.AttachCurrentThread();
					first.Wait();
					ctx.AtSafeBoundary();
					second.Wait();
					// dispose happens here from `using`
				} catch (Exception caught) {
					ex = caught;
					first.ForceSet();
					second.ForceSet();
				}
			}
		);
		thread.Start();

		Assert.True(first.Entered.Wait(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
		if (ex is not null)
			throw ex;
		asset.QueueReload(TestContext.Current.CancellationToken);
		store.AtSafeBoundary();
		int published = store.ApplyQueuedReloadsOrThrow();
		Assert.Equal(1, published);
		Assert.Equal($"{ownerId}::asset", v.Val);

		store.AtSafeBoundary();
		first.Proceed();
		Assert.True(second.Entered.Wait(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
		if (ex is not null)
			throw ex;
		Assert.Throws<AssetLeaseExpiredException>(() => _ = v.Val);

		second.Proceed();
		Assert.True(thread.Join(TimeSpan.FromMilliseconds(100)));
		if (ex is not null)
			throw ex;
	}

	[Fact]
	public static void DisposingContextAllowsReclamation() {
		AssetStore store = new();
		using AssetThreadCtx mainCtx = store.AttachCurrentThread();
		store.RegisterSource(new TestSource(), "source");
		store.RegisterResolver(new TestResolver(), "resolver");
		store.RegisterStagedCreator(new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		TestAsset v = asset.Borrow().Value;

		ThreadCheckpoint ckp = new();
		Exception? ex = null;
		Thread thread = new(() => {
				try {
					using AssetThreadCtx ctx = store.AttachCurrentThread();
					ckp.Wait();
					// dispose happens here from `using`
				} catch (Exception caught) {
					ex = caught;
					ckp.ForceSet();
				}
			}
		);
		thread.Start();

		Assert.True(ckp.Entered.Wait(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
		if (ex is not null)
			throw ex;
		asset.QueueReload(TestContext.Current.CancellationToken);
		store.AtSafeBoundary();
		int published = store.ApplyQueuedReloadsOrThrow();
		Assert.Equal(1, published);
		Assert.Equal($"{ownerId}::asset", v.Val);

		store.AtSafeBoundary();
		ckp.Proceed();
		Assert.True(thread.Join(TimeSpan.FromMilliseconds(100)));
		if (ex is not null)
			throw ex;
		Assert.Throws<AssetLeaseExpiredException>(() => _ = v.Val);
	}
}
