// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Internals.Tests.Assets;

public sealed class AssetStoreCycleTests {
	private const string ownerId = "test";

	[Fact]
	public void AcyclicChainSucceeds() {
		AssetStore store = new();
		AssetLoadingResolver resolver = new(
			store,
			new Dictionary<AssetId, AssetId> {
				[new AssetId(ownerId, "assetA")] = new(ownerId, "assetB"),
				[new AssetId(ownerId, "assetB")] = new(ownerId, "assetC"),
			}
		);
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, resolver, "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "assetA"));
		asset.Warm(TestContext.Current.CancellationToken);
	}

	[Fact]
	public void SelfCycleThrows() {
		AssetStore store = new();
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(
			ownerId,
			new AssetLoadingResolver(
				store,
				new Dictionary<AssetId, AssetId> {
					[new AssetId(ownerId, "assetA")] = new(ownerId, "assetA"),
				}
			),
			"resolver"
		);
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "assetA"));
		AssetLoadCycleException ex = Assert.Throws<AssetLoadCycleException>(() => asset.Warm(TestContext.Current.CancellationToken));
		Assert.Contains($"{nameof(TestAsset)}({ownerId}::assetA) -> {nameof(TestAsset)}({ownerId}::assetA)", ex.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void LongerCyclesThrow() {
		AssetStore store = new();
		AssetLoadingResolver resolver = new(
			store,
			new Dictionary<AssetId, AssetId> {
				[new AssetId(ownerId, "assetA")] = new(ownerId, "assetB"),
				[new AssetId(ownerId, "assetB")] = new(ownerId, "assetA"),
			}
		);
		store.RegisterSource(ownerId, new TestSource(), "source");
		store.RegisterResolver(ownerId, resolver, "resolver");
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "assetA"));
		AssetLoadCycleException ex = Assert.Throws<AssetLoadCycleException>(() => asset.Warm(TestContext.Current.CancellationToken));
		Assert.Contains(
			$"{nameof(TestAsset)}({ownerId}::assetA) -> {nameof(TestAsset)}({ownerId}::assetB) -> {nameof(TestAsset)}({ownerId}::assetA)",
			ex.Message,
			StringComparison.Ordinal
		);

		resolver.Map = new Dictionary<AssetId, AssetId> {
			[new AssetId(ownerId, "assetA")] = new(ownerId, "assetB"),
			[new AssetId(ownerId, "assetB")] = new(ownerId, "assetC"),
			[new AssetId(ownerId, "assetC")] = new(ownerId, "assetD"),
			[new AssetId(ownerId, "assetD")] = new(ownerId, "assetE"),
			[new AssetId(ownerId, "assetE")] = new(ownerId, "assetA"),
		};

		asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "assetA"));
		ex = Assert.Throws<AssetLoadCycleException>(() => asset.Warm(TestContext.Current.CancellationToken));
		Assert.Contains(
			$"{nameof(TestAsset)}({ownerId}::assetA) -> {nameof(TestAsset)}({ownerId}::assetB) -> {nameof(TestAsset)}({ownerId}::assetC) -> {nameof(TestAsset)}({ownerId}::assetD) -> {nameof(TestAsset)}({ownerId}::assetE) -> {nameof(TestAsset)}({ownerId}::assetA)",
			ex.Message,
			StringComparison.Ordinal
		);
	}
}
