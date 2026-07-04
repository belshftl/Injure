// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;

namespace Injure.Internals.Tests.Assets;

public sealed class AssetStoreDependencyTests {
	private const string ownerId = "test";

	[Fact]
	public void ResolverNotHandledDoesntLeakDeps() {
		AssetStore store = new();
		store.RegisterSource(ownerId, new TestSource(new TestDependency("dep-a")), "source");
		store.RegisterResolver(ownerId, new FetchThenNotHandledResolver(new TestDependency("dep-b")), "resolver-a", localPriority: -1);
		store.RegisterResolver(ownerId, new TestResolver(new TestDependency("dep-c")), "resolver-b", localPriority: 0);
		store.RegisterStagedCreator(ownerId, new TestCreator(), "creator");

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		AssetLease<TestAsset> lease = asset.Borrow();
		Assert.Equal([new TestDependency("dep-a"), new TestDependency("dep-c")], lease.Dependencies.CastDepsToArray<TestDependency>());
	}

	[Fact]
	public void CreatorNotHandledDoesntLeakDeps() {
		AssetStore store = new();
		store.RegisterSource(ownerId, new TestSource(new TestDependency("dep-a")), "source");
		store.RegisterResolver(ownerId, new TestResolver(), "resolver");
		store.RegisterCreator(ownerId, new SteppingCreator(new Step("step1", Handled: false, new TestDependency("dep-b"))), "creator-a", localPriority: -1);
		store.RegisterCreator(ownerId, new SteppingCreator(new Step("step1", Handled: true, new TestDependency("dep-c"))), "creator-b", localPriority: 0);

		AssetRef<TestAsset> asset = store.GetAsset<TestAsset>(new AssetId(ownerId, "asset"));
		AssetLease<TestAsset> lease = asset.Borrow();
		Assert.Equal([new TestDependency("dep-a"), new TestDependency("dep-c")], lease.Dependencies.CastDepsToArray<TestDependency>());
	}
}
