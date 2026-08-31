// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Injure.Mods.Runtime.Modif.CallDispatch;

/// <summary>
/// Contains methods relevant to the indirect call dispatch mechanism.
/// </summary>
/// <remarks>
/// Public so that it can be called into by emitted IL; not for direct use.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IndirectCallDispatch {
	private sealed class SlotEntry {
		public IntPtr Target;
		public MethodInfo? Method;
		public string? Description;
	}

	private static SlotEntry?[] slots = new SlotEntry?[4];
	private static int nextSlot = 0;
	private static readonly Dictionary<RuntimeMethodHandle, int> byTarget = new();
	private static readonly Lock @lock = new();

	internal static int TableSize => Volatile.Read(ref slots).Length;

	/// <summary>
	/// Gets the entry point for a slot's target.
	/// </summary>
	/// <exception cref="DispatchUnavailableException">
	/// Thrown if the slot has been cleared or has never been used in the first place.
	/// </exception>
	public static IntPtr GetTarget(int slot) {
		SlotEntry?[] s = Volatile.Read(ref slots);
		if (unchecked((uint)slot >= (uint)s.Length) || s[slot] is not SlotEntry entry)
			throw new DispatchUnavailableException(slot, null);
		return entry.Target == IntPtr.Zero ? throw new DispatchUnavailableException(slot, entry.Description) : entry.Target;
	}

	/// <summary>
	/// Gets the method a slot points at. For diagnostics.
	/// </summary>
	public static MethodInfo? GetTargetMethod(int slot) {
		SlotEntry?[] s = Volatile.Read(ref slots);
		return unchecked((uint)slot < (uint)s.Length) ? s[slot]?.Method : null;
	}

	/// <summary>
	/// Reserves a slot for a method, or returns one already reserved for that method.
	/// </summary>
	internal static int AllocateSlot(MethodInfo target) {
		InternalStateException.ThrowIfNull(target);
		RuntimeMethodHandle handle = target.MethodHandle;
		lock (@lock) {
			if (byTarget.TryGetValue(handle, out int existing))
				return existing;
			if (nextSlot == slots.Length) {
				var grown = new SlotEntry[slots.Length * 2];
				slots.CopyTo(grown, 0);
				grown[nextSlot] = new SlotEntry() {
					Method = target,
					Description = $"{target.DeclaringType}::{target.Name}",
					Target = functionPointerOf(target),
				};
				Volatile.Write(ref slots, grown);
			} else if (nextSlot < slots.Length) {
				slots[nextSlot] = new SlotEntry() {
					Method = target,
					Description = $"{target.DeclaringType}::{target.Name}",
					Target = functionPointerOf(target),
				};
			} else {
				throw new InternalStateException("nextSlot went past slots.Length");
			}
			byTarget[handle] = nextSlot;
			return nextSlot++;
		}
	}

	/// <summary>
	/// Clears every slot targeting a method in an assembly.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Part of mod unload; must run before the methods calling through those slots are reverted.
	/// </para>
	/// <para>
	/// The slot itself is kept, since a patched body just has a <c>ldc.i4</c> literal and assigning
	/// that number to a different target would silently reroute it to somewhere unrelated, whereas a
	/// cleared slot throws instead.
	/// </para>
	/// </remarks>
	internal static void ClearForAssembly(Assembly assembly) {
		InternalStateException.ThrowIfNull(assembly);
		lock (@lock) {
			foreach (SlotEntry? entry in slots) {
				if (entry is null)
					return;
				if (entry.Method is not MethodInfo method || method.Module.Assembly != assembly)
					continue;
				byTarget.Remove(method.MethodHandle);
				Volatile.Write(ref entry.Target, IntPtr.Zero);
				entry.Method = null;
			}
		}
	}

	/// <summary>
	/// Whether a slot still has a target.
	/// </summary>
	internal static bool IsLive(int slot) {
		SlotEntry?[] s = slots;
		return (uint)slot < (uint)s.Length && s[slot]?.Target != IntPtr.Zero;
	}

	private static IntPtr functionPointerOf(MethodInfo target) {
		RuntimeHelpers.PrepareMethod(target.MethodHandle);
		return target.MethodHandle.GetFunctionPointer();
	}
}
