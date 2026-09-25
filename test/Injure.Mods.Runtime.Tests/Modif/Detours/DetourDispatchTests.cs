// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using Injure.Mods.Runtime.Modif.Detours;

namespace Injure.Mods.Runtime.Tests.Modif.Detours;

public sealed class DetourDispatchTests {
	private static int nextFakeEntry = 0x1000;

	/// <summary>
	/// Allocates a slot with a chain installed, so <see cref="DetourDispatch.Enter"/> can tell running
	/// the chain (nonzero) apart from falling through (zero).
	/// </summary>
	/// <remarks>
	/// The entry is never called, so any nonzero value works.
	/// </remarks>
	private static int mkSlot() {
		int slot = DetourDispatch.AllocateSlot();
		DetourDispatch.SetChain(slot, Interlocked.Increment(ref nextFakeEntry), new object());
		return slot;
	}

	private static bool runsChain(int slot) => DetourDispatch.Enter(slot) != 0;

	// ==========================================================================================
	// bypass tokens
	[Fact]
	public static void AnEntryWithNoTokenRunsTheChain() =>
		Assert.True(runsChain(mkSlot()));

	[Fact]
	public static void ATokenIsConsumedExactlyOnce() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);

		Assert.False(runsChain(slot));

		// a recursive call from inside the original body should run the chain again
		Assert.True(runsChain(slot));

		DetourDispatch.UnwindBypass(depth);
	}

	[Fact]
	public static void ATokenIsOnlyConsumedByItsOwnSlot() {
		int outer = mkSlot();
		int other = mkSlot();
		int depth = DetourDispatch.PushBypass(outer);

		Assert.True(runsChain(other));
		Assert.False(runsChain(outer));

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
		Assert.True(runsChain(outer));
		Assert.False(runsChain(inner));
		Assert.False(runsChain(outer));

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
		Assert.True(runsChain(slot));
	}

	[Fact]
	public static void UnwindingBelowTheCurrentDepthDoesNothing() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);

		DetourDispatch.UnwindBypass(depth + 42);

		Assert.False(runsChain(slot));
	}

	[Fact]
	public static void TokensAreNotSharedBetweenThreads() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);
		bool otherThreadRunsChain = false;

		Thread t = new(() => otherThreadRunsChain = runsChain(slot));
		t.Start();
		t.Join();

		Assert.True(otherThreadRunsChain);
		Assert.False(runsChain(slot));
		DetourDispatch.UnwindBypass(depth);
	}

	[Fact]
	public static void TheStackIsGrownPastInitialCapacity() {
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);
		for (int i = 0; i < 64; i++)
			DetourDispatch.PushBypass(slot);

		for (int i = 0; i < 65; i++)
			Assert.False(runsChain(slot));

		Assert.True(runsChain(slot));
		DetourDispatch.UnwindBypass(depth);
	}

	// ==========================================================================================
	// empty slots
	[Fact]
	public static void AnEntryToAnEmptySlotFallsThrough() =>
		Assert.Equal(0, DetourDispatch.Enter(DetourDispatch.AllocateSlot()));

	[Fact]
	public static void AnEntryToAClearedSlotFallsThrough() {
		int slot = mkSlot();

		DetourDispatch.ClearChain(slot);

		Assert.Equal(0, DetourDispatch.Enter(slot));
	}

	[Fact]
	public static void ATokenIsConsumedEvenIfItsChainWasClearedAfterThePush() {
		// a terminus whose chain is cleared while it's mid-call still has to consume its token, or
		// the token would be left for a later entry, which would then skip its chain
		int slot = mkSlot();
		int depth = DetourDispatch.PushBypass(slot);

		DetourDispatch.ClearChain(slot);
		Assert.Equal(0, DetourDispatch.Enter(slot));

		DetourDispatch.SetChain(slot, 0x1234, new object());
		Assert.True(runsChain(slot));

		DetourDispatch.UnwindBypass(depth);
	}

	[Fact]
	public static void AnEntryToAnEmptySlotLeavesOtherSlotsTokensAlone() {
		int outer = mkSlot();
		int empty = DetourDispatch.AllocateSlot();
		int depth = DetourDispatch.PushBypass(outer);

		Assert.Equal(0, DetourDispatch.Enter(empty));

		// still there, so this entry consumes it rather than running the chain
		Assert.False(runsChain(outer));

		DetourDispatch.UnwindBypass(depth);
	}

	// ==========================================================================================
	// slots
	[Fact]
	public static void SlotsAreDistinctAndStartEmpty() {
		int first = DetourDispatch.AllocateSlot();
		int second = DetourDispatch.AllocateSlot();

		Assert.NotEqual(first, second);
		Assert.False(DetourDispatch.HasChain(first));
		Assert.Equal(0, DetourDispatch.Enter(first));
		Assert.Null(DetourDispatch.GetChainState(first));
	}

	[Fact]
	public static void AChainIsReadableOnceSet() {
		int slot = DetourDispatch.AllocateSlot();
		object state = new();

		DetourDispatch.SetChain(slot, 0x1234, state);

		Assert.True(DetourDispatch.HasChain(slot));
		Assert.Equal(0x1234, DetourDispatch.Enter(slot));
		Assert.Same(state, DetourDispatch.GetChainState(slot));
	}

	[Fact]
	public static void ChainReplacementIsVisibleImmediately() {
		int slot = DetourDispatch.AllocateSlot();
		DetourDispatch.SetChain(slot, 0x1111, new object());
		object replacement = new();

		DetourDispatch.SetChain(slot, 0x2222, replacement);

		// the prologue rereads this on every call, which is why adding a detour needs no ReJIT
		Assert.Equal(0x2222, DetourDispatch.Enter(slot));
		Assert.Same(replacement, DetourDispatch.GetChainState(slot));
	}

	[Fact]
	public static void ClearingASlotStopsNewEntriesReachingTheChain() {
		int slot = mkSlot();

		DetourDispatch.ClearChain(slot);

		Assert.False(DetourDispatch.HasChain(slot));
		Assert.Equal(0, DetourDispatch.Enter(slot));
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
		int slot = DetourDispatch.AllocateSlot();
		object state = new();
		DetourDispatch.SetChain(slot, 0x1234, state);
		return (slot, new WeakReference(state));
	}

	[Fact]
	public static void OutOfRangeSlotHasNothing() {
		Assert.False(DetourDispatch.HasChain(int.MaxValue));
		Assert.Equal(0, DetourDispatch.Enter(int.MaxValue));
		Assert.Null(DetourDispatch.GetChainState(int.MaxValue));
	}
}
