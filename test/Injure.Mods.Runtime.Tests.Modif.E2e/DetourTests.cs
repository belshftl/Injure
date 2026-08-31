// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif;
using Injure.Mods.Runtime.Modif.Detours;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Tests.Modif.E2e;

[Collection(E2eCollection.Name)]
public sealed class DetourTests(E2eFixture fixture) {
	private readonly E2eFixture fxt = fixture;

	private delegate int next_Compute(int value);

	private static int add10(next_Compute next, int value) => next(value) + 10;
	private static int @double(next_Compute next, int value) => next(value) * 2;

	private static MethodInfo method(string name) =>
		typeof(DetourTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

	// ============================================================================================
	private static class SingleTarget {
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Compute(int value) => value + 1;
	}

	[Fact]
	public void SingleDetourWorks() {
		MethodIdentity m = fxt.GetIdentity(typeof(SingleTarget).GetMethod(
			nameof(SingleTarget.Compute),
			BindingFlags.Static | BindingFlags.Public
		)!);
		fxt.Registry.AddDetour(m, new DetourRegistration("e2e.detour.single", "detour", method(nameof(add10))));

		ApplyResult result = fxt.Orchestrator.ApplyPending();

		Assert.Contains(m, result.Applied);
		Assert.Equal(14, SingleTarget.Compute(3)); // Compute(3) is 4, +10 is 14
	}

	// ============================================================================================
	private static class ChainTarget {
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Compute(int value) => value + 1;
	}

	[Fact]
	public void DetourChainRunsInRegistrationOrder() {
		MethodIdentity m = fxt.GetIdentity(typeof(ChainTarget).GetMethod(
			nameof(ChainTarget.Compute),
			BindingFlags.Static | BindingFlags.Public
		)!);
		fxt.Registry.AddDetour(m, new DetourRegistration("e2e.detour.chain", "a", method(nameof(add10))));
		fxt.Registry.AddDetour(m, new DetourRegistration("e2e.detour.chain", "b", method(nameof(@double))));

		fxt.Orchestrator.ApplyPending();

		// Compute(3) is 4, doubled is 8, +10 is 18; reverse order would yield 28
		Assert.Equal(18, ChainTarget.Compute(3));
	}

	// ============================================================================================
	private static class RemoveTarget {
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Compute(int value) => value + 1;
	}

	[Fact]
	public void RemovingTheLastDetourRemovesThePrologue() {
		MethodIdentity m = fxt.GetIdentity(typeof(RemoveTarget).GetMethod(
			nameof(RemoveTarget.Compute),
			BindingFlags.Static | BindingFlags.Public
		)!);
		fxt.Registry.AddDetour(m, new DetourRegistration("e2e.detour.remove", "detour", method(nameof(add10))));
		fxt.Orchestrator.ApplyPending();
		Assert.Equal(14, RemoveTarget.Compute(3));

		fxt.Registry.RemoveOwner("e2e.detour.remove");
		ApplyResult result = fxt.Orchestrator.ApplyPending();

		Assert.Contains(m, result.Reverted);
		Assert.Equal(4, RemoveTarget.Compute(3));
	}

	// ============================================================================================
	private static class CombinedTarget {
		public static int Slot = 1;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Read() => Slot;
	}

	private delegate int next_Read();

	private static int add100(next_Read next) => next() + 100;

	private static IlManipulatorRegistration writesToSlot(int value) =>
		IlManipulatorRegistration.Create<E2eL>("e2e.combined", "patch", ctx => {
			IlFieldRef slot = IlRefFactory.Field(typeof(CombinedTarget).GetField(
				nameof(CombinedTarget.Slot),
				BindingFlags.Static | BindingFlags.Public
			)!);
			ctx.EmitAtStart(e => {
				e.LdcI4(value);
				e.Stsfld(slot);
			});
		});

	[Fact]
	public void PatchAndDetourOnTheSameMethodCompose() {
		MethodIdentity m = fxt.GetIdentity(typeof(CombinedTarget).GetMethod(
			nameof(CombinedTarget.Read),
			BindingFlags.Static | BindingFlags.Public
		)!);
		fxt.Registry.AddManipulator(m, writesToSlot(5));
		fxt.Registry.AddDetour(m, new DetourRegistration("e2e.combined", "detour", method(nameof(add100))));

		fxt.Orchestrator.ApplyPending();

		// the patch writes 5 to Slot before the original read runs, then the detour adds 100
		CombinedTarget.Slot = -1;
		Assert.Equal(105, CombinedTarget.Read());
	}
}
