// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using Injure.Mods.Runtime.Modif.Detours;

namespace Injure.Mods.Runtime.Tests.Modif.Detours;

public sealed class DetourReentrancyTests {
	private static int slot = -1;
	private static int originalCalls;
	private static int chainCalls;

	/// <remarks>
	/// Written by hand to match what <see cref="DetourTransform"/> emits, so recursion semantics can
	/// be tested without a profiler.
	/// </remarks>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int dispatched(int value) {
		if (!DetourDispatch.EnterAndCheck(slot))
			return original(value);
		chainCalls++;
		return chain(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int original(int value) {
		originalCalls++;
		return value + 1;
	}

	/// <remarks>
	/// Written by hand to match what <see cref="DetourChain"/> emits, so recursion semantics can
	/// be tested without a profiler.
	/// </remarks>
	private static int chain(int value) {
		int depth = DetourDispatch.PushBypass(slot);
		try {
			return dispatched(value) * 2;
		} finally {
			DetourDispatch.UnwindBypass(depth);
		}
	}

	private static void reset() {
		slot = DetourDispatch.AllocateSlot();
		originalCalls = 0;
		chainCalls = 0;
	}

	[Fact]
	public static void AnEntryRunsTheChainWhichReachesTheOriginal() {
		reset();

		int result = dispatched(3);

		Assert.Equal(8, result);
		Assert.Equal(1, chainCalls);
		Assert.Equal(1, originalCalls);
	}

	[Fact]
	public static void TokenLeftByAFailedCallDoesntAffectTheNextEntry() {
		reset();
		int depth = DetourDispatch.PushBypass(slot);
		DetourDispatch.UnwindBypass(depth);

		dispatched(3);

		Assert.Equal(1, chainCalls);
	}

	[Fact]
	public static void RepeatedEntriesEachRunTheChainOnce() {
		reset();

		dispatched(1);
		dispatched(2);
		dispatched(3);

		Assert.Equal(3, chainCalls);
		Assert.Equal(3, originalCalls);
	}

	[Fact]
	public static void UnrelatedDetouredMethodEnteredMidwayThroughKeepsItsOwnToken() {
		reset();
		int other = DetourDispatch.AllocateSlot();
		int depth = DetourDispatch.PushBypass(slot);

		// standing in for a type initializer that enters another detoured method between the push and
		// the call the token was meant for
		Assert.True(DetourDispatch.EnterAndCheck(other));

		Assert.False(DetourDispatch.EnterAndCheck(slot));
		DetourDispatch.UnwindBypass(depth);
	}
}
