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

	private static OwnerOrderedEntry<DetourRegistration> entry(DetourRegistration reg) =>
		new(reg, reg.OwnerId, reg.LocalId);

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
		fxt.Registry.AddDetour(m, entry(new DetourRegistration("e2e.detour.single", "detour", method(nameof(add10)))));

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
	public void DetourChainRunsInOrder() {
		MethodIdentity m = fxt.GetIdentity(typeof(ChainTarget).GetMethod(
			nameof(ChainTarget.Compute),
			BindingFlags.Static | BindingFlags.Public
		)!);
		fxt.Registry.AddDetour(m, entry(new DetourRegistration("e2e.detour.chain", "a", method(nameof(add10)))));
		fxt.Registry.AddDetour(m, entry(new DetourRegistration("e2e.detour.chain", "b", method(nameof(@double)))));

		fxt.Orchestrator.ApplyPending();

		// Compute() detours to add10()
		// -> add10() calls next(), i.e. double()
		//   -> double() calls next() which is the original Compute()
		//     -> Compute(3) is 4
		//   -> 4 doubled is 8
		// -> 8 + 10 is 18
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
		fxt.Registry.AddDetour(m, entry(new DetourRegistration("e2e.detour.remove", "detour", method(nameof(add10)))));
		fxt.Orchestrator.ApplyPending();
		Assert.Equal(14, RemoveTarget.Compute(3));

		fxt.Registry.RemoveOwner("e2e.detour.remove");
		ApplyResult result = fxt.Orchestrator.ApplyPending();

		Assert.Contains(m, result.Reverted);
		Assert.Equal(4, RemoveTarget.Compute(3));
	}

	// ============================================================================================
	private struct ValueTarget {
		public int Field;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Incr() => ++Field;
	}

	private delegate int next_Incr(ref ValueTarget self);
	private static int incr3(next_Incr next, ref ValueTarget self) {
		self.Field += 3;
		return next(ref self) + next(ref self) + 1;
	}

	[Fact]
	public void MutationsThroughAByrefValueTypeReceiverPersist() {
		MethodIdentity m = fxt.GetIdentity(typeof(ValueTarget).GetMethod(
			nameof(ValueTarget.Incr),
			BindingFlags.Instance | BindingFlags.Public
		)!);
		fxt.Registry.AddDetour(m, entry(new DetourRegistration("e2e.detour.valuetype", "detour", method(nameof(incr3)))));

		ApplyResult result = fxt.Orchestrator.ApplyPending();

		ValueTarget v = new() { Field = -1 };
		Assert.Equal(8, v.Incr()); // -1 + 3 = 2, (2 + 1) + ((2 + 1) + 1) + 1 = 8
		Assert.Equal(4, v.Field); // field has been incremented by 3 and incremented by 1 twice
	}

	// ============================================================================================
	private struct WidenValueTarget {
		public int Field;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Incr() => ++Field;
	}

	private delegate int next_WidenValueTarget_Incr(ref WidenValueTarget self);
	private static int widenIncr3(next_WidenValueTarget_Incr next, ref WidenValueTarget self) {
		self.Field += 3;
		return next(ref self) + next(ref self) + 1;
	}
	private static int widenMul2(Func<object, int> next, object self) {
		ref WidenValueTarget v = ref Unsafe.Unbox<WidenValueTarget>(self);
		v.Field *= 2;
		return next(self);
	}

	[Fact]
	public void ByrefValueTypeReceiverCanBeWidenedToObject() {
		MethodIdentity m = fxt.GetIdentity(typeof(WidenValueTarget).GetMethod(
			nameof(WidenValueTarget.Incr),
			BindingFlags.Instance | BindingFlags.Public
		)!);
		fxt.Registry.AddDetour(m, entry(new DetourRegistration("e2e.detour.widenvaluetype", "a", method(nameof(widenMul2)))));
		fxt.Registry.AddDetour(m, entry(new DetourRegistration("e2e.detour.widenvaluetype", "b", method(nameof(widenIncr3)))));

		ApplyResult result = fxt.Orchestrator.ApplyPending();

		WidenValueTarget v = new() { Field = 2 };
		Assert.Equal(18, v.Incr()); // 2 * 2 = 4, 4 + 3 = 7, (7 + 1) + ((7 + 1) + 1) + 1 = 18
		Assert.Equal(9, v.Field); // field has been multiplied by 2, incremented by 3, and incremented by 1 twice
	}

	// ============================================================================================
	private static class CombinedTarget {
		public static int Slot = 1;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Read() => Slot;
	}

	private delegate int next_Read();

	private static int add100(next_Read next) => next() + 100;

	private static OwnerOrderedEntry<IlManipulatorRegistration> writesToSlot(int value) {
		var reg = IlManipulatorRegistration.Create<E2eL>("e2e.combined", "patch", ctx => {
			IlFieldRef slot = IlRefFactory.Field(typeof(CombinedTarget).GetField(
				nameof(CombinedTarget.Slot),
				BindingFlags.Static | BindingFlags.Public
			)!);
			ctx.EmitAtStart(e => {
				e.LdcI4(value);
				e.Stsfld(slot);
			});
		});
		return new OwnerOrderedEntry<IlManipulatorRegistration>(reg, "e2e.combined", "patch");
	}

	[Fact]
	public void PatchAndDetourOnTheSameMethodCompose() {
		MethodIdentity m = fxt.GetIdentity(typeof(CombinedTarget).GetMethod(
			nameof(CombinedTarget.Read),
			BindingFlags.Static | BindingFlags.Public
		)!);
		fxt.Registry.AddManipulator(m, writesToSlot(5));
		fxt.Registry.AddDetour(m, entry(new DetourRegistration("e2e.combined", "detour", method(nameof(add100)))));

		fxt.Orchestrator.ApplyPending();

		// the patch writes 5 to Slot before the original read runs, then the detour adds 100
		CombinedTarget.Slot = -1;
		Assert.Equal(105, CombinedTarget.Read());
	}
}
