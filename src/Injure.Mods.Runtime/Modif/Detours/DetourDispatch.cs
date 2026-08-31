// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Injure.Mods.Runtime.Modif.Detours;

/// <summary>
/// The runtime half of a detour, containing methods the emitted detour prologue calls into.
/// </summary>
/// <remarks>
/// <para>
/// A detoured method starts with a prologue that looks up whether it should run the detour chain
/// or the original body. Everything that decides that lives here, reached by a slot number, so the
/// emitted IL carries no reference to anything but this class and needs no metadata from any mod.
/// </para>
/// <para>
/// The last in chain re-enters the target method to reach the original body. Without a marker,
/// that would recurse forever, so the terminus pushes a bypass token naming its slot, and the
/// prologue consumes it and falls through.
/// </para>
/// <para>
/// The token is a stack rather than a single value, and carries its slot rather than being a flag.
/// The window between pushing and the prologue reading it looks empty but isn't; the call can trigger
/// JIT of the target and its type initializer, which is arbitrary code that may call another detoured
/// method. If it was a plain flag, it would be consumed by that inner method, or overwritten by its
/// terminus, and the outer call would then run its whole chain again, creating an unbounded recursion
/// with a very hard to trace down origin.
/// </para>
/// <para>
/// Public so that it can be called into by emitted IL; not for direct use.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DetourDispatch {
	private sealed class SlotEntry {
		public IntPtr Entry;
		public object? State;
	}

	/// <summary>
	/// Initial depth of a thread's bypass stack.
	/// </summary>
	/// <remarks>
	/// The amount of detoured methods that a thread can be partway into entering at once should be
	/// pretty small; if this value is ever wrong, the array is grown.
	/// </remarks>
	private const int initialBypassCapacity = 8;

	[ThreadStatic] private static int[]? bypassSlots;
	[ThreadStatic] private static int bypassDepth;

	private static SlotEntry?[] slots = new SlotEntry?[4];
	private static int nextSlot = 0;
	private static readonly Lock @lock = new();

	/// <summary>
	/// Whether the calling entry should run the detour chain.
	/// </summary>
	/// <remarks>
	/// Returns <see langword="false"/> exactly once per token pushed for this slot, which is how the
	/// chain's terminus reaches the original body. Any other entry, including a recursive call made
	/// from inside the original body, runs the chain, like how a trampoline-based detour would behave.
	/// </remarks>
	public static bool EnterAndCheck(int slot) {
		int depth = bypassDepth;
		if (depth > 0 && bypassSlots![depth - 1] == slot) {
			bypassDepth = depth - 1;
			return false;
		}
		return true;
	}

	/// <summary>
	/// The entry point of a slot's detour chain.
	/// </summary>
	/// <remarks>
	/// Read fresh on every call, which is why registering a second detour on an already-detoured
	/// method changes only this table and needs no ReJIT.
	/// </remarks>
	public static IntPtr GetChainEntry(int slot) {
		SlotEntry?[] s = Volatile.Read(ref slots);
		return (uint)slot < (uint)s.Length ? s[slot]?.Entry ?? IntPtr.Zero : IntPtr.Zero;
	}

	// ==========================================================================================
	// bypass tokens

	/// <summary>
	/// Marks the next entry to a slot as belonging to the chain's terminus.
	/// </summary>
	/// <returns>
	/// The stack depth before the push, to be passed to <see cref="UnwindBypass"/> once the call
	/// returns.
	/// </returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int PushBypass(int slot) {
		int[] stack = bypassSlots ??= new int[initialBypassCapacity];
		int depth = bypassDepth;
		if (depth == stack.Length) {
			Array.Resize(ref stack, stack.Length * 2);
			bypassSlots = stack;
		}
		stack[depth] = slot;
		bypassDepth = depth + 1;
		return depth;
	}

	/// <summary>
	/// Discards any tokens left above a depth.
	/// </summary>
	/// <remarks>
	/// Called from the terminus's <c>finally</c>. Normally the prologue has already consumed the
	/// token and this does nothing, but a call that throws before reaching the prologue (e.g.
	/// a failing type initializer) would otherwise leave a token that makes some later entry
	/// skip its chain. Truncating to a recorded depth rather than popping once also cleans up after an
	/// inner terminus that threw.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void UnwindBypass(int depth) {
		if (bypassDepth > depth)
			bypassDepth = depth;
	}

	// ==========================================================================================
	// slots

	/// <summary>
	/// Reserves a slot for a method.
	/// </summary>
	/// <remarks>
	/// Slots are never reused. A patched method's prologue holds its slot as a literal, and reusing a
	/// slot whose method has been reverted would hand its chain to whatever code is still running the
	/// old version.
	/// </remarks>
	internal static int AllocateSlot() {
		lock (@lock) {
			if (nextSlot == slots.Length) {
				var grown = new SlotEntry[slots.Length * 2];
				slots.CopyTo(grown, 0);
				grown[nextSlot] = new SlotEntry();
				Volatile.Write(ref slots, grown);
			} else if (nextSlot < slots.Length) {
				slots[nextSlot] = new SlotEntry();
			} else {
				throw new InternalStateException("nextSlot went past slots.Length");
			}
			return nextSlot++;
		}
	}

	/// <summary>
	/// Points a slot at a chain.
	/// </summary>
	/// <param name="slot">The slot.</param>
	/// <param name="entry">The chain's entry point.</param>
	/// <param name="state">
	/// The chain's head state. The head thunk reads this back through
	/// <see cref="GetChainState"/> so it can be closure-free and therefore callable through a
	/// function pointer.
	/// </param>
	/// <remarks>
	/// <para>
	/// <paramref name="state"/> is not bookkeeping. <paramref name="entry"/> is a raw pointer, and a
	/// mod's assembly lives in a collectible load context that unloads the moment nothing managed
	/// refers to it, leaving the pointer aimed at freed memory while patched methods still call
	/// through it. Holding a managed reference here is what prevents that, and dropping it is what
	/// permits an unload. Managed references across load contexts are fine; only metadata references
	/// are not.
	/// </para>
	/// <para>
	/// The state must transitively retain the whole chain, since nothing else does.
	/// </para>
	/// </remarks>
	internal static void SetChain(int slot, IntPtr entry, object state) {
		ArgumentNullException.ThrowIfNull(state);
		SlotEntry target = Volatile.Read(ref slots)[slot]!;
		lock (@lock) {
			// write first, the head reads the state as soon as anything can reach the entry, so a
			// reader that sees a non-zero entry has already seen a complete state
			Volatile.Write(ref target.State, state);
			Volatile.Write(ref target.Entry, entry);
		}
	}

	/// <summary>
	/// The state a slot's head thunk works from.
	/// </summary>
	/// <remarks>
	/// Called from generated IL as the first thing a chain's head does, so it's a plain array read.
	/// Returns <see langword="null"/> for a slot with no chain, which a head can only observe if it
	/// was reached after its chain was cleared, i.e. a call already in flight when the slot was cleared.
	/// </remarks>
	internal static object? GetChainState(int slot) {
		SlotEntry?[] s = Volatile.Read(ref slots);
		return (uint)slot < (uint)s.Length ? s[slot]?.State : null;
	}

	/// <summary>
	/// Clears a slot and releases what its chain retained.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ordering is load-bearing. Clear the slot first so no new entry can reach the chain, then revert
	/// the method, then wait for code still running the old version to drain, and only then let the
	/// retained references go. Getting it wrong jumps into freed memory or frees memory from which
	/// code is currently executing.
	/// </para>
	/// <para>
	/// A caller that reverts a method must clear its slot even though the reverted body no longer
	/// mentions it, because a frame already inside the old version can still call through.
	/// </para>
	/// </remarks>
	internal static void ClearChain(int slot) {
		SlotEntry target = Volatile.Read(ref slots)[slot]!;
		lock (@lock) {
			Volatile.Write(ref target.Entry, IntPtr.Zero);
			Volatile.Write(ref target.State, null);
		}
	}

	/// <summary>
	/// Whether a slot currently has a chain.
	/// </summary>
	internal static bool HasChain(int slot) {
		SlotEntry?[] s = Volatile.Read(ref slots);
		return (uint)slot < (uint)s.Length && s[slot]?.Entry != IntPtr.Zero;
	}
}
