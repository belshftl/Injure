// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Tests.Assets;

public sealed class AssetStoreDependencyTests {
	private const string ownerId = "test";

	[Fact]
	public static void ResolverNotHandledDoesntLeakDeps() {
		AssetStore store = new();
		store.RegisterSource(new TestSource(new TestDependency("dep-a")), "source");
		store.RegisterResolver(new FetchThenNotHandledResolver(new TestDependency("dep-b")), "resolver-a", localOrder: -1);
		store.RegisterResolver(new TestResolver(new TestDependency("dep-c")), "resolver-b", localOrder: 0);
		store.RegisterStagedCreator(new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		AssetLease<TestAsset> lease = asset.Borrow();
		Assert.Equal([new TestDependency("dep-a"), new TestDependency("dep-c")], lease.Dependencies.CastDepsToArray<TestDependency>());
	}

	[Fact]
	public static void CreatorNotHandledDoesntLeakDeps() {
		AssetStore store = new();
		store.RegisterSource(new TestSource(new TestDependency("dep-a")), "source");
		store.RegisterResolver(new TestResolver(), "resolver");
		store.RegisterCreator(new SteppingCreator(new Step("step1", Handled: false, new TestDependency("dep-b"))), "creator-a", localOrder: -1);
		store.RegisterCreator(new SteppingCreator(new Step("step1", Handled: true, new TestDependency("dep-c"))), "creator-b", localOrder: 0);

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		AssetLease<TestAsset> lease = asset.Borrow();
		Assert.Equal([new TestDependency("dep-a"), new TestDependency("dep-c")], lease.Dependencies.CastDepsToArray<TestDependency>());
	}
}
