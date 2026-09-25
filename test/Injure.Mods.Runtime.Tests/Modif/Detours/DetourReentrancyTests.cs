// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using Injure.Mods.Runtime.Modif.Detours;

namespace Injure.Mods.Runtime.Tests.Modif.Detours;

public sealed unsafe class DetourReentrancyTests {
	private static int slot = -1;
	private static int originalCalls;
	private static int chainCalls;
	private static bool clearMidCall;

	/// <remarks>
	/// Written by hand to match what <see cref="DetourTransform"/> emits, so recursion semantics can
	/// be tested without a profiler: one <see cref="DetourDispatch.Enter"/> call, then either the
	/// original body or a <c>calli</c> through the returned entry.
	/// </remarks>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int dispatched(int value) {
		nint entry = DetourDispatch.Enter(slot);
		if (entry == 0)
			return original(value);
		return ((delegate*<int, int>)entry)(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int original(int value) {
		originalCalls++;
		return value + 1;
	}

	/// <remarks>
	/// Written by hand to match what <see cref="DetourChain"/> emits for its terminus, so recursion
	/// semantics can be tested without a profiler.
	/// </remarks>
	private static int chain(int value) {
		chainCalls++;
		int depth = DetourDispatch.PushBypass(slot);
		try {
			// stands in for a clear landing while this call is in flight
			if (clearMidCall)
				DetourDispatch.ClearChain(slot);
			return dispatched(value) * 2;
		} finally {
			DetourDispatch.UnwindBypass(depth);
		}
	}

	private static void install() => DetourDispatch.SetChain(slot, (nint)(delegate*<int, int>)&chain, new object());

	private static void reset() {
		slot = DetourDispatch.AllocateSlot();
		originalCalls = 0;
		chainCalls = 0;
		clearMidCall = false;
		install();
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
		DetourDispatch.SetChain(other, 0x1234, new object());
		int depth = DetourDispatch.PushBypass(slot);

		// standing in for a type initializer that enters another detoured method between the push and
		// the call the token was meant for
		Assert.NotEqual(0, DetourDispatch.Enter(other));

		Assert.Equal(0, DetourDispatch.Enter(slot));
		DetourDispatch.UnwindBypass(depth);
	}

	[Fact]
	public static void AClearedChainFallsThroughToTheOriginal() {
		reset();
		DetourDispatch.ClearChain(slot);

		int result = dispatched(3);

		Assert.Equal(4, result);
		Assert.Equal(0, chainCalls);
		Assert.Equal(1, originalCalls);
	}

	[Fact]
	public static void AChainClearedMidCallStillReachesTheOriginalOnce() {
		reset();
		clearMidCall = true;

		// the terminus's token must still be consumed by the re-entry, even though the slot is empty
		// by the time the re-entry happens
		Assert.Equal(8, dispatched(3));
		Assert.Equal(1, chainCalls);
		Assert.Equal(1, originalCalls);

		// nothing left behind: an entry now falls through, and reinstalling runs the chain again
		clearMidCall = false;
		Assert.Equal(4, dispatched(3));
		Assert.Equal(1, chainCalls);

		install();
		Assert.Equal(8, dispatched(3));
		Assert.Equal(2, chainCalls);
		Assert.Equal(3, originalCalls);
	}
}
