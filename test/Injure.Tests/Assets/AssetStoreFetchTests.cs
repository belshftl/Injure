// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreFetchTests {
	private const string ownerId = "test";

	[Fact]
	public static void OptionalTryFetchReturnsNullForUnhandledAsset() {
		AssetStore store = new();
		DictionarySource source = new();
		AssetId mainID = new(ownerId, "main");
		AssetId optionalID = new(ownerId, "missing");
		OptionalExtraFetchResolver resolver = new(optionalID);
		source.Set(mainID, "main-value");
		store.RegisterSource(source, "source");
		store.RegisterResolver(resolver, "resolver");
		store.RegisterStagedCreator(new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(mainID);
		asset.Warm(TestContext.Current.CancellationToken);

		Assert.False(resolver.SawOptionalStream);
	}

	[Fact]
	public static void RequiredFetchThrowsForUnhandledAsset() {
		AssetStore store = new();
		DictionarySource source = new();
		AssetId mainID = new(ownerId, "main");
		AssetId extraID = new(ownerId, "missing");
		source.Set(mainID, "main-value");
		store.RegisterSource(source, "source");
		store.RegisterResolver(new RequiredExtraFetchResolver(extraID), "resolver");
		store.RegisterStagedCreator(new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(mainID);
		Assert.Throws<AssetUnhandledException>(() => asset.Warm(TestContext.Current.CancellationToken));
	}

	[Fact]
	public static void NonSeekableSourceStreamIsReplacedAndOriginalIsDisposed() {
		AssetStore store = new();
		NonSeekableSource source = new();
		store.RegisterSource(source, "source");
		store.RegisterResolver(new TestResolver(), "resolver");
		store.RegisterStagedCreator(new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		asset.Warm(TestContext.Current.CancellationToken);

		Assert.NotNull(source.LastStream);
		Assert.True(source.LastStream.Disposed);
	}
}
