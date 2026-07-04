// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Internals.Tests.Assets;

public sealed class AssetStoreFetchTests {
	private const string ownerId = "test";

	[Fact]
	public void OptionalTryFetchReturnsNullForUnhandledAsset() {
		AssetStore store = new();
		DictionarySource source = new();
		AssetId mainID = new(ownerId, "main");
		AssetId optionalID = new(ownerId, "missing");
		OptionalExtraFetchResolver resolver = new(optionalID);
		source.Set(mainID, "main-value");
		store.RegisterSource(ownerId, source, "source");
		store.RegisterResolver(ownerId, resolver, "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(mainID);
		asset.Warm();

		Assert.False(resolver.SawOptionalStream);
	}

	[Fact]
	public void RequiredFetchThrowsForUnhandledAsset() {
		AssetStore store = new();
		DictionarySource source = new();
		AssetId mainID = new(ownerId, "main");
		AssetId extraID = new(ownerId, "missing");
		source.Set(mainID, "main-value");
		store.RegisterSource(ownerId, source, "source");
		store.RegisterResolver(ownerId, new RequiredExtraFetchResolver(extraID), "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(mainID);
		Assert.Throws<AssetUnhandledException>(() => asset.Warm());
	}

	[Fact]
	public void NonSeekableSourceStreamIsReplacedAndOriginalIsDisposed() {
		AssetStore store = new();
		NonSeekableSource source = new();
		store.RegisterSource(ownerId, source, "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		asset.Warm();

		Assert.NotNull(source.LastStream);
		Assert.True(source.LastStream.Disposed);
	}
}
