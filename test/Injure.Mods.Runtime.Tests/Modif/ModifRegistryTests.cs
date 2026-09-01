// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Abstractions.Modif.Il.Metadata;
using Injure.Mods.Runtime.Modif;
using Injure.Mods.Runtime.Modif.Detours;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Tests.Modif;

public sealed class ModifRegistryTests {
	private static readonly MethodIdentity methodA = new(new ModuleId(1), 0x06000001);
	private static readonly MethodIdentity methodB = new(new ModuleId(1), 0x06000002);
	private static readonly MethodIdentity otherModule = new(new ModuleId(2), 0x06000001);

	private static IlManipulatorRegistration makeManipulator(string ownerId, string localId) =>
		IlManipulatorRegistration.Create<TestL>(ownerId, localId, _ => { });

	private static DetourRegistration makeDetour(string ownerId, string localId) =>
		new(ownerId, localId, impl);

	private static readonly MethodBase impl =
		typeof(ModifRegistryTests).GetMethod(nameof(stub), BindingFlags.Static | BindingFlags.NonPublic)!;

	private static void stub() {
	}

	private static OwnerOrderedEntry<IlManipulatorRegistration> entry(
		IlManipulatorRegistration registration,
		int localOrder = 0,
		IEnumerable<OwnerOrderingConstraint>? before = null,
		IEnumerable<OwnerOrderingConstraint>? after = null
	) => new(registration, registration.OwnerId, registration.LocalId, localOrder, before, after);

	private static OwnerOrderedEntry<DetourRegistration> entry(
		DetourRegistration registration,
		int localOrder = 0,
		IEnumerable<OwnerOrderingConstraint>? before = null,
		IEnumerable<OwnerOrderingConstraint>? after = null
	) => new(registration, registration.OwnerId, registration.LocalId, localOrder, before, after);

	// ==========================================================================================
	// generations
	[Fact]
	public static void UnregisteredMethodHasNoGeneration() {
		ModifRegistry registry = new();

		Assert.Equal(MethodGeneration.None, registry.GetGeneration(methodA));
		Assert.False(registry.GetGeneration(methodA).IsModified);
		Assert.Empty(registry.GetManipulators(methodA));
		Assert.Empty(registry.GetDetours(methodA));
	}

	[Fact]
	public static void EachManipulatorBumpsThePatchGeneration() {
		ModifRegistry registry = new();

		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		Assert.Equal(new MethodGeneration(1, false), registry.GetGeneration(methodA));

		registry.AddManipulator(methodA, entry(makeManipulator("first", "b")));
		Assert.Equal(new MethodGeneration(2, false), registry.GetGeneration(methodA));
	}

	[Fact]
	public static void OnlyFirstDetourChangesGeneration() {
		ModifRegistry registry = new();

		registry.AddDetour(methodA, entry(makeDetour("first", "a")));
		MethodGeneration afterFirst = registry.GetGeneration(methodA);
		registry.AddDetour(methodA, entry(makeDetour("first", "b")));
		registry.AddDetour(methodA, entry(makeDetour("second", "c")));

		Assert.True(afterFirst.HasDetourPrologue);
		Assert.Equal(afterFirst, registry.GetGeneration(methodA));
	}

	[Fact]
	public static void OnlyLastDetourRemovalChangesGeneration() {
		ModifRegistry registry = new();
		registry.AddDetour(methodA, entry(makeDetour("first", "a")));
		registry.AddDetour(methodA, entry(makeDetour("first", "b")));
		MethodGeneration detoured = registry.GetGeneration(methodA);

		registry.Remove(methodA, "first", "b");
		Assert.Equal(detoured, registry.GetGeneration(methodA));

		registry.Remove(methodA, "first", "a");
		Assert.False(registry.GetGeneration(methodA).HasDetourPrologue);
	}

	[Fact]
	public static void PatchAndDetourGenerationsAreIndependent() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.AddDetour(methodA, entry(makeDetour("first", "d")));

		Assert.Equal(new MethodGeneration(1, true), registry.GetGeneration(methodA));

		registry.AddManipulator(methodA, entry(makeManipulator("first", "b")));
		Assert.Equal(new MethodGeneration(2, true), registry.GetGeneration(methodA));
	}

	[Fact]
	public static void RemovingAManipulatorBumpsNotRewinds() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.AddManipulator(methodA, entry(makeManipulator("first", "b")));

		registry.Remove(methodA, "first", "b");

		Assert.Equal(3, registry.GetGeneration(methodA).Patch);
	}

	[Fact]
	public static void GenerationsAreTrackedPerMethod() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.AddManipulator(methodA, entry(makeManipulator("first", "b")));
		registry.AddManipulator(methodB, entry(makeManipulator("first", "a")));

		Assert.Equal(2, registry.GetGeneration(methodA).Patch);
		Assert.Equal(1, registry.GetGeneration(methodB).Patch);
	}

	// ==========================================================================================
	// ordering
	[Fact]
	public static void ManipulatorsSortByOwnerIdThenLocalIdWithNoConstraints() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("charlie", "a")));
		registry.AddManipulator(methodA, entry(makeManipulator("alpha", "a")));
		registry.AddManipulator(methodA, entry(makeManipulator("bravo", "a")));

		Assert.Equal(
			["alpha", "bravo", "charlie"],
			registry.GetManipulators(methodA).Select(m => m.OwnerId)
		);
	}

	[Fact]
	public static void OneOwnersManipulatorEntriesSortByLocalIdWithNoConstraints() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "c")));
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.AddManipulator(methodA, entry(makeManipulator("first", "b")));

		Assert.Equal(
			["a", "b", "c"],
			registry.GetManipulators(methodA).Select(m => m.LocalId)
		);
	}

	[Fact]
	public static void DetoursAlsoSortByOwnerIdThenLocalIdWithNoConstraints() {
		ModifRegistry registry = new();
		registry.AddDetour(methodA, entry(makeDetour("charlie", "a")));
		registry.AddDetour(methodA, entry(makeDetour("alpha", "a")));
		registry.AddDetour(methodA, entry(makeDetour("bravo", "a")));

		Assert.Equal(
			["alpha", "bravo", "charlie"],
			registry.GetDetours(methodA).Select(m => m.OwnerId)
		);
	}

	[Fact]
	public static void OneOwnersDetourEntriesAlsoSortByLocalIdWithNoConstraints() {
		ModifRegistry registry = new();
		registry.AddDetour(methodA, entry(makeDetour("first", "c")));
		registry.AddDetour(methodA, entry(makeDetour("first", "a")));
		registry.AddDetour(methodA, entry(makeDetour("first", "b")));

		Assert.Equal(
			["a", "b", "c"],
			registry.GetDetours(methodA).Select(m => m.LocalId)
		);
	}

	[Fact]
	public static void LowerLocalOrderSortsFirst() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "z-first"), localOrder: 0));
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a-second"), localOrder: 1));

		Assert.Equal(["z-first", "a-second"], registry.GetManipulators(methodA).Select(m => m.LocalId));
	}

	// ==========================================================================================
	// id pair uniqueness
	[Fact]
	public static void DuplicateManipulatorIdPairIsRejected() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));

		Assert.Throws<InternalStateException>(() => registry.AddManipulator(methodA, entry(makeManipulator("first", "a"))));
	}

	[Fact]
	public static void DuplicateDetourIdPairIsRejected() {
		ModifRegistry registry = new();
		registry.AddDetour(methodA, entry(makeDetour("first", "a")));

		Assert.Throws<InternalStateException>(() => registry.AddDetour(methodA, entry(makeDetour("first", "a"))));
	}

	[Fact]
	public static void IdPairIsExclusiveToOneModification() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "shared")));

		Assert.Throws<InternalStateException>(() => registry.AddDetour(methodA, entry(makeDetour("first", "shared"))));
	}

	[Fact]
	public static void IdPairIsExclusiveToOneModificationInTheOtherDirectionToo() {
		ModifRegistry registry = new();
		registry.AddDetour(methodA, entry(makeDetour("first", "shared")));

		Assert.Throws<InternalStateException>(() => registry.AddManipulator(methodA, entry(makeManipulator("first", "shared"))));
	}

	[Fact]
	public static void IdPairsAreScopedToOneMethod() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.AddManipulator(methodB, entry(makeManipulator("first", "a")));

		Assert.Single(registry.GetManipulators(methodA));
		Assert.Single(registry.GetManipulators(methodB));
	}

	[Fact]
	public static void IdPairIsReusableOnceRemoved() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.Remove(methodA, "first", "a");

		registry.AddDetour(methodA, entry(makeDetour("first", "a")));

		Assert.Single(registry.GetDetours(methodA));
	}

	[Fact]
	public static void WrapperCantDisagreeWithTheRegistrationItWraps() {
		ModifRegistry registry = new();
		IlManipulatorRegistration registration = makeManipulator("first", "a");
		OwnerOrderedEntry<IlManipulatorRegistration> mismatched = new(registration, "first", "b");

		Assert.Throws<InternalStateException>(() => registry.AddManipulator(methodA, mismatched));
	}

	// ==========================================================================================
	// removal
	[Fact]
	public static void RemovingOwnerReportsOnlyMethodsItTouched() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.AddManipulator(methodB, entry(makeManipulator("second", "a")));
		registry.AddDetour(otherModule, entry(makeDetour("first", "d")));

		ImmutableArray<MethodIdentity> affected = registry.RemoveOwner("first");

		Assert.Equal([methodA, otherModule], affected.OrderBy(m => m.Module.Value).ToArray());
		Assert.Single(registry.GetManipulators(methodB));
	}

	[Fact]
	public static void RemovingOwnerTakesBothKinds() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "m")));
		registry.AddDetour(methodA, entry(makeDetour("first", "d")));
		registry.AddManipulator(methodA, entry(makeManipulator("second", "m")));

		registry.RemoveOwner("first");

		Assert.Equal(["second"], registry.GetManipulators(methodA).Select(m => m.OwnerId));
		Assert.Empty(registry.GetDetours(methodA));
		Assert.False(registry.GetGeneration(methodA).HasDetourPrologue);
	}

	[Fact]
	public static void RemovingOwnerWithNoRegistrationsReportsNothing() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));

		Assert.Empty(registry.RemoveOwner("second"));
	}

	[Fact]
	public static void RemovingMissingModificationReportsFalse() {
		ModifRegistry registry = new();

		Assert.False(registry.Remove(methodA, "first", "a"));
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		Assert.False(registry.Remove(methodA, "first", "b"));
		Assert.True(registry.Remove(methodA, "first", "a"));
	}

	[Fact]
	public static void MethodWithNothingLeftIsForgotten() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.Remove(methodA, "first", "a");

		Assert.Empty(registry.ModifiedMethods);
		// the entry is gone, so a later registration starts over rather than continuing
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		Assert.Equal(1, registry.GetGeneration(methodA).Patch);
	}

	[Fact]
	public static void RemovingModuleDropsItsMethodsWithoutDirtyingThem() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.AddManipulator(otherModule, entry(makeManipulator("first", "a")));
		registry.DrainDirty();

		registry.RemoveModule(otherModule.Module);

		Assert.Equal([methodA], registry.ModifiedMethods);
		Assert.Empty(registry.DrainDirty());
	}

	[Fact]
	public static void RemovingModuleClearsPendingDirtyEntriesForIt() {
		ModifRegistry registry = new();
		registry.AddManipulator(otherModule, entry(makeManipulator("first", "a")));

		registry.RemoveModule(otherModule.Module);

		Assert.Empty(registry.DrainDirty());
	}

	// ==========================================================================================
	// dirty tracking
	[Fact]
	public static void BatchOfRegistrationsDrainsAsOneSetOfMethods() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.AddManipulator(methodA, entry(makeManipulator("first", "b")));
		registry.AddManipulator(methodB, entry(makeManipulator("first", "a")));

		ImmutableArray<MethodIdentity> drained = registry.DrainDirty();

		Assert.Equal(2, drained.Length);
		Assert.Contains(methodA, drained);
		Assert.Contains(methodB, drained);
	}

	[Fact]
	public static void DrainingIsDestructive() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));

		Assert.Single(registry.DrainDirty());
		Assert.Empty(registry.DrainDirty());
	}

	[Fact]
	public static void ChangeAfterADrainReappears() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.DrainDirty();

		registry.AddManipulator(methodA, entry(makeManipulator("first", "b")));

		Assert.Equal([methodA], registry.DrainDirty());
	}

	[Fact]
	public static void RejectedRegistrationDirtiesNothing() {
		ModifRegistry registry = new();
		registry.AddManipulator(methodA, entry(makeManipulator("first", "a")));
		registry.DrainDirty();

		Assert.Throws<InternalStateException>(() => registry.AddManipulator(methodA, entry(makeManipulator("first", "a"))));

		Assert.Empty(registry.DrainDirty());
	}

	[Fact]
	public static void AnAdditionalDetourDirtiesNothing() {
		ModifRegistry registry = new();
		registry.AddDetour(methodA, entry(makeDetour("first", "a")));
		registry.DrainDirty();

		registry.AddDetour(methodA, entry(makeDetour("first", "b")));

		// the prologue reads the chain head at run time, so the emitted body is unchanged
		Assert.Empty(registry.DrainDirty());
	}
}

public sealed class MethodTransformCacheTests {
	private static readonly MethodIdentity methodA = new(new ModuleId(1), 0x06000001);
	private static readonly MethodIdentity methodB = new(new ModuleId(2), 0x06000002);

	private static IlMethodBody makeBody() =>
		IlMethodBody.CreateEmpty(IlRefFactory.Method(typeof(object).GetMethod(
			nameof(ToString), BindingFlags.Instance | BindingFlags.Public
		)!));

	private static IlEncodedMethodBody makeEncoded(params byte[] bytes) => new(bytes, 1, bytes.Length - 1, 0);

	// ==========================================================================================
	// baseline
	[Fact]
	public static void BaselineIsReturnedAsStored() {
		MethodTransformCache cache = new();
		IlMethodBody baseline = makeBody();
		cache.SetBaseline(methodA, baseline);

		Assert.True(cache.TryGetBaseline(methodA, out IlMethodBody stored));
		Assert.Same(baseline, stored);
	}

	[Fact]
	public static void MissingBaselineMisses() {
		MethodTransformCache cache = new();

		Assert.False(cache.TryGetBaseline(methodA, out _));
	}

	// ==========================================================================================
	// generation matching
	[Fact]
	public static void TransformedBodyHitsOnlyItsOwnGeneration() {
		MethodTransformCache cache = new();
		cache.SetTransformed(methodA, 3, makeBody());

		Assert.True(cache.TryGetTransformed(methodA, 3, out _));
		Assert.False(cache.TryGetTransformed(methodA, 2, out _));
		Assert.False(cache.TryGetTransformed(methodA, 4, out _));
	}

	[Fact]
	public static void EncodedBodyHitsOnlyItsOwnGeneration() {
		MethodTransformCache cache = new();
		MethodGeneration generation = new(2, false);
		cache.SetEncoded(methodA, generation, makeEncoded(0x02, 0x2a));

		Assert.True(cache.TryGetEncoded(methodA, generation, out _));
		Assert.False(cache.TryGetEncoded(methodA, new MethodGeneration(3, false), out _));
	}

	[Fact]
	public static void DetourFlagInvalidatesEncodedButNotTransformed() {
		MethodTransformCache cache = new();
		cache.SetTransformed(methodA, 1, makeBody());
		cache.SetEncoded(methodA, new MethodGeneration(1, false), makeEncoded(0x02, 0x2a));

		// registering a first detour flips the flag and leaves the patch generation alone
		Assert.False(cache.TryGetEncoded(methodA, new MethodGeneration(1, true), out _));
		Assert.True(cache.TryGetTransformed(methodA, 1, out _));
	}

	[Fact]
	public static void NewerGenerationReplacesTheOlderEntry() {
		MethodTransformCache cache = new();
		IlMethodBody first = makeBody();
		IlMethodBody second = makeBody();
		cache.SetTransformed(methodA, 1, first);
		cache.SetTransformed(methodA, 2, second);

		Assert.False(cache.TryGetTransformed(methodA, 1, out _));
		Assert.True(cache.TryGetTransformed(methodA, 2, out IlMethodBody stored));
		Assert.Same(second, stored);
	}

	[Fact]
	public static void GenerationZeroIsDistinguishableFromNothingCached() {
		MethodTransformCache cache = new();

		Assert.False(cache.TryGetTransformed(methodA, 0, out _));
		cache.SetTransformed(methodA, 0, makeBody());
		Assert.True(cache.TryGetTransformed(methodA, 0, out _));
	}

	[Fact]
	public static void EntriesAreIndependentPerMethod() {
		MethodTransformCache cache = new();
		cache.SetTransformed(methodA, 1, makeBody());

		Assert.False(cache.TryGetTransformed(methodB, 1, out _));
	}

	// ==========================================================================================
	// eviction
	[Fact]
	public static void EvictingDerivedKeepsTheBaseline() {
		MethodTransformCache cache = new();
		cache.SetBaseline(methodA, makeBody());
		cache.SetTransformed(methodA, 1, makeBody());
		cache.SetEncoded(methodA, new MethodGeneration(1, false), makeEncoded(0x02, 0x2a));

		cache.EvictDerived(methodA);

		Assert.True(cache.TryGetBaseline(methodA, out _));
		Assert.False(cache.TryGetTransformed(methodA, 1, out _));
		Assert.False(cache.TryGetEncoded(methodA, new MethodGeneration(1, false), out _));
	}

	[Fact]
	public static void EvictingDerivedWithNoBaselineDropsTheEntry() {
		MethodTransformCache cache = new();
		cache.SetTransformed(methodA, 1, makeBody());

		cache.EvictDerived(methodA);

		Assert.Equal(0, cache.Count);
	}

	[Fact]
	public static void EvictingAMethodDropsEverythingForIt() {
		MethodTransformCache cache = new();
		cache.SetBaseline(methodA, makeBody());
		cache.SetBaseline(methodB, makeBody());

		cache.Evict(methodA);

		Assert.False(cache.TryGetBaseline(methodA, out _));
		Assert.True(cache.TryGetBaseline(methodB, out _));
	}

	[Fact]
	public static void EvictingAModuleDropsEveryMethodInIt() {
		MethodTransformCache cache = new();
		MethodIdentity sibling = new(methodA.Module, 0x06000009);
		cache.SetBaseline(methodA, makeBody());
		cache.SetBaseline(sibling, makeBody());
		cache.SetBaseline(methodB, makeBody());

		cache.EvictModule(methodA.Module);

		Assert.Equal(1, cache.Count);
		Assert.True(cache.TryGetBaseline(methodB, out _));
	}

	[Fact]
	public static void ClearingDropsEverything() {
		MethodTransformCache cache = new();
		cache.SetBaseline(methodA, makeBody());
		cache.SetBaseline(methodB, makeBody());

		cache.Clear();

		Assert.Equal(0, cache.Count);
	}
}
