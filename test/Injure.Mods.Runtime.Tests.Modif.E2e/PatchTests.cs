// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Tests.Modif.E2e;

[Collection(E2eCollection.Name)]
public sealed class PatchTests(E2eFixture fixture) {
	private readonly E2eFixture fxt = fixture;

	private static OwnerOrderedEntry<IlManipulatorRegistration> writesToSlot(string ownerId, string localId, Type targetType, int value) {
		var reg = IlManipulatorRegistration.Create<E2eL>(ownerId, localId, ctx => {
			IlFieldRef slot = IlRefFactory.Field(targetType.GetField(
				nameof(Target.Slot),
				BindingFlags.Static | BindingFlags.Public
			)!);
			ctx.EmitAtStart(e => {
				e.LdcI4(value);
				e.Stsfld(slot);
			});
		});
		return new OwnerOrderedEntry<IlManipulatorRegistration>(reg, ownerId, localId);
	}

	// ============================================================================================
	private static class Target {
		public static int Slot;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Compute() => Slot;
	}


	[Fact]
	public void PatchWorks() {
		MethodIdentity m = fxt.GetIdentity(typeof(Target).GetMethod(
			nameof(Target.Compute),
			BindingFlags.Static | BindingFlags.Public
		)!);
		fxt.Registry.AddManipulator(m, writesToSlot("e2e.patch", "patch", typeof(Target), 42));

		ApplyResult result = fxt.Orchestrator.ApplyPending();

		Assert.Contains(m, result.Applied);
		Target.Slot = -1; // baseline the field so the read proves the write happened, not luck
		Assert.Equal(42, Target.Compute());
	}

	// ============================================================================================
	private static class RevertTarget {
		public static int Slot;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int Compute() => Slot;
	}

	[Fact]
	public void RemovingTheOwnerRevertsToTheOriginalBody() {
		MethodIdentity m = fxt.GetIdentity(typeof(RevertTarget).GetMethod(
			nameof(RevertTarget.Compute),
			BindingFlags.Static | BindingFlags.Public
		)!);
		fxt.Registry.AddManipulator(m, writesToSlot("e2e.revert", "patch", typeof(RevertTarget), 7));
		fxt.Orchestrator.ApplyPending();
		RevertTarget.Slot = -1;
		Assert.Equal(7, RevertTarget.Compute());

		fxt.Registry.RemoveOwner("e2e.revert");
		ApplyResult result = fxt.Orchestrator.ApplyPending();

		Assert.Contains(m, result.Reverted);
		RevertTarget.Slot = 123;
		Assert.Equal(123, RevertTarget.Compute());
	}
}
