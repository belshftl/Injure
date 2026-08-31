// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using Injure.Mods.Runtime.Modif.Detours;

namespace Injure.Mods.Runtime.Tests.Modif.Detours;

public sealed class DetourDispatchTests {
	private static int mkSlot() => DetourDispatch.AllocateSlot();

	[Fact]
	public static void AnEntryWithNoTokenRunsTheChain() =>
		Assert.True(DetourDispatch.EnterAndCheck(mkSlot()));

	[Fact]
	public static void ATokenIsConsumedExactlyOnce() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);

		Assert.False(DetourDispatch.EnterAndCheck(slot));

		// a recursive call from inside the original body should run the chain again
		Assert.True(DetourDispatch.EnterAndCheck(slot));

		DetourDispatch.UnwindBypass(depth);
	}

	[Fact]
	public static void ATokenIsOnlyConsumedByItsOwnSlot() {
		int outer = mkSlot();
		int other = mkSlot();
		int depth = DetourDispatch.PushBypass(outer);

		Assert.True(DetourDispatch.EnterAndCheck(other));
		Assert.False(DetourDispatch.EnterAndCheck(outer));

		DetourDispatch.UnwindBypass(depth);
	}

	[Fact]
	public static void NestedTokensUnwindCorrectly() {
		int outer = mkSlot();
		int inner = mkSlot();
		int depth = DetourDispatch.PushBypass(outer);
		DetourDispatch.PushBypass(inner);

		// similar to what a type initializer would produce, i.e. entering another detoured method
		// between the push and the call the token was meant for
		Assert.True(DetourDispatch.EnterAndCheck(outer));
		Assert.False(DetourDispatch.EnterAndCheck(inner));
		Assert.False(DetourDispatch.EnterAndCheck(outer));

		DetourDispatch.UnwindBypass(depth);
	}

	[Fact]
	public static void UnwindingDiscardsLeftovers() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);
		DetourDispatch.PushBypass(slot);
		DetourDispatch.PushBypass(slot);

		DetourDispatch.UnwindBypass(depth);

		// call that threw before reaching the prologue should not leave behind a token
		Assert.True(DetourDispatch.EnterAndCheck(slot));
	}

	[Fact]
	public static void UnwindingBelowTheCurrentDepthDoesNothing() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);

		DetourDispatch.UnwindBypass(depth + 42);

		Assert.False(DetourDispatch.EnterAndCheck(slot));
	}

	[Fact]
	public static void TokensAreNotSharedBetweenThreads() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);
		bool otherThreadRunsChain = false;

		Thread t = new(() => otherThreadRunsChain = DetourDispatch.EnterAndCheck(slot));
		t.Start();
		t.Join();

		Assert.True(otherThreadRunsChain);
		Assert.False(DetourDispatch.EnterAndCheck(slot));
		DetourDispatch.UnwindBypass(depth);
	}

	[Fact]
	public static void TheStackIsGrownPastInitialCapacity() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);
		for (int i = 0; i < 64; i++)
			DetourDispatch.PushBypass(slot);

		for (int i = 0; i < 65; i++)
			Assert.False(DetourDispatch.EnterAndCheck(slot));

		Assert.True(DetourDispatch.EnterAndCheck(slot));
		DetourDispatch.UnwindBypass(depth);
	}

	// ==========================================================================================
	// slots
	[Fact]
	public static void SlotsAreDistinctAndStartEmpty() {
		int first = mkSlot();
		int second = mkSlot();

		Assert.NotEqual(first, second);
		Assert.False(DetourDispatch.HasChain(first));
		Assert.Equal(IntPtr.Zero, DetourDispatch.GetChainEntry(first));
		Assert.Null(DetourDispatch.GetChainState(first));
	}

	[Fact]
	public static void AChainIsReadableOnceSet() {
		int slot = mkSlot();
		object state = new();

		DetourDispatch.SetChain(slot, 0x1234, state);

		Assert.True(DetourDispatch.HasChain(slot));
		Assert.Equal(0x1234, DetourDispatch.GetChainEntry(slot));
		Assert.Same(state, DetourDispatch.GetChainState(slot));
	}

	[Fact]
	public static void ChainReplacementIsVisibleImmediately() {
		int slot = mkSlot();
		DetourDispatch.SetChain(slot, 0x1111, new object());
		object replacement = new();

		DetourDispatch.SetChain(slot, 0x2222, replacement);

		// the prologue rereads this on every call, which is why adding a detour needs no ReJIT
		Assert.Equal(0x2222, DetourDispatch.GetChainEntry(slot));
		Assert.Same(replacement, DetourDispatch.GetChainState(slot));
	}

	[Fact]
	public static void ClearingASlotStopsNewEntriesReachingTheChain() {
		int slot = mkSlot();
		DetourDispatch.SetChain(slot, 0x1234, new object());

		DetourDispatch.ClearChain(slot);

		Assert.False(DetourDispatch.HasChain(slot));
		Assert.Equal(IntPtr.Zero, DetourDispatch.GetChainEntry(slot));
		Assert.Null(DetourDispatch.GetChainState(slot));
	}

	[Fact]
	public static void ClearingASlotReleasesWhatItRetained() {
		(int slot, WeakReference state) = setChainAndDrop();

		DetourDispatch.ClearChain(slot);
		for (int i = 0; i < 10 && state.IsAlive; i++) {
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
			GC.WaitForPendingFinalizers();
		}

		Assert.False(state.IsAlive);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (int Slot, WeakReference State) setChainAndDrop() {
		int slot = mkSlot();
		object state = new();
		DetourDispatch.SetChain(slot, 0x1234, state);
		return (slot, new WeakReference(state));
	}

	[Fact]
	public static void OutOfRangeSlotHasNothing() {
		Assert.False(DetourDispatch.HasChain(int.MaxValue));
		Assert.Equal(IntPtr.Zero, DetourDispatch.GetChainEntry(int.MaxValue));
		Assert.Null(DetourDispatch.GetChainState(int.MaxValue));
	}
}
