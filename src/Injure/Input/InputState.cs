// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Diagnostics;
using Injure.Host;

namespace Injure.Input;

/// <summary>
/// Which keys are held down.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and has every key released, same as
/// <see cref="Rest"/>.
/// </remarks>
public readonly struct KeyboardState {
	private readonly ulong keys0;
	private readonly ulong keys1;
	private readonly ulong keys2;
	private readonly ulong keys3;

	/// <summary>
	/// The state with every key released.
	/// </summary>
	public static readonly KeyboardState Rest = default;

	/// <summary>
	/// Creates a state with exactly the keys in <paramref name="down"/> held down.
	/// </summary>
	public KeyboardState(ReadOnlySpan<Key> down) {
		foreach (Key key in down) {
			int idx = (int)key.Tag;
			// Key is a [ClosedEnum], and as of right now, doesn't have values numerically above 255
			if ((uint)idx > 0xffu)
				throw new InternalStateException($"key '{key}' doesn't fit into the 256-bit bitset");
			int word = idx >> 6;
			int bit = idx & 0b111111;
			switch (word) {
			case 0:
				keys0 |= 1ul << bit;
				break;
			case 1:
				keys1 |= 1ul << bit;
				break;
			case 2:
				keys2 |= 1ul << bit;
				break;
			case 3:
				keys3 |= 1ul << bit;
				break;
			default: throw new UnreachableException(); // if 0 <= x <= 255 then x >> 6 can't be above 3
			}
		}
	}

	/// <inheritdoc cref="KeyboardState(ReadOnlySpan{Key})"/>
	public KeyboardState(params Key[] down) : this(down.AsSpan()) {
	}

	internal KeyboardState(ulong bitset0, ulong bitset1, ulong bitset2, ulong bitset3) {
		keys0 = bitset0;
		keys1 = bitset1;
		keys2 = bitset2;
		keys3 = bitset3;
	}

	/// <summary>
	/// Whether <paramref name="key"/> is held down.
	/// </summary>
	public bool IsDown(Key key) {
		int idx = (int)key.Tag;
		if ((uint)idx > 0xffu)
			return false;
		int word = idx >> 6;
		int bit = idx & 0b111111;
		return word switch {
			0 => (keys0 & 1ul << bit) != 0,
			1 => (keys1 & 1ul << bit) != 0,
			2 => (keys2 & 1ul << bit) != 0,
			3 => (keys3 & 1ul << bit) != 0,
			_ => throw new UnreachableException(), // if 0 <= x <= 255 then x >> 6 can't be above 3
		};
	}

	/// <summary>Whether either Ctrl key is held down.</summary>
	public bool Ctrl => IsDown(Key.LeftCtrl) || IsDown(Key.RightCtrl);

	/// <summary>Whether either Shift key is held down.</summary>
	public bool Shift => IsDown(Key.LeftShift) || IsDown(Key.RightShift);

	/// <summary>Whether either Alt (Option on macOS) key is held down.</summary>
	public bool Alt => IsDown(Key.LeftAlt) || IsDown(Key.RightAlt);

	/// <summary>Whether either GUI (Windows, Command, Super) key is held down.</summary>
	public bool Gui => IsDown(Key.LeftGui) || IsDown(Key.RightGui);
}

/// <summary>
/// The buttons and axes of one gamepad.
/// </summary>
/// <remarks>
/// <para>
/// Stick axes are in [-1, 1], with +X pointing right and +Y pointing down (up = -Y, matching
/// screen coordinates). Trigger axes are in [0, 1], 0 being released.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and has every button released and every axis at
/// 0, same as <see cref="Rest"/>.
/// </para>
/// </remarks>
public readonly struct GamepadState {
	private readonly uint buttons;

	/// <summary>
	/// Left stick X axis, in [-1, 1].
	/// </summary>
	public float LeftX { get; }

	/// <summary>
	/// Left stick Y axis, in [-1, 1]; +Y is down.
	/// </summary>
	public float LeftY { get; }

	/// <summary>
	/// Right stick X axis, in [-1, 1].
	/// </summary>
	public float RightX { get; }

	/// <summary>
	/// Right stick Y axis, in [-1, 1]; +Y is down.
	/// </summary>
	public float RightY { get; }

	/// <summary>
	/// Left trigger, in [0, 1].
	/// </summary>
	public float LeftTrigger { get; }

	/// <summary>
	/// Right trigger, in [0, 1].
	/// </summary>
	public float RightTrigger { get; }

	/// <summary>
	/// The state with every button released and every axis at 0.
	/// </summary>
	public static readonly GamepadState Rest = default;

	/// <summary>
	/// Creates a state with exactly the buttons in <paramref name="down"/> held down and the given
	/// axis values.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if a stick axis is outside [-1, 1] or a trigger is outside [0, 1].
	/// </exception>
	public GamepadState(
		ReadOnlySpan<GamepadButton> down,
		float leftX,
		float leftY,
		float rightX,
		float rightY,
		float leftTrigger,
		float rightTrigger
	) {
		static void checkStick(float v, string paramName) {
			if (v < -1f || v > 1f)
				throw new ArgumentOutOfRangeException(paramName, "stick axis values must be within [-1, +1]");
		}
		static void checkTrigger(float v, string paramName) {
			if (v < 0f || v > 1f)
				throw new ArgumentOutOfRangeException(paramName, "trigger axis values must be within [0, +1]");
		}

		checkStick(leftX, nameof(leftX));
		checkStick(leftY, nameof(leftY));
		checkStick(rightX, nameof(rightX));
		checkStick(rightY, nameof(rightY));
		checkTrigger(leftTrigger, nameof(leftTrigger));
		checkTrigger(rightTrigger, nameof(rightTrigger));

		foreach (GamepadButton btn in down) {
			int idx = (int)btn.Tag;
			// GamepadButton is a [ClosedEnum], and as of right now, doesn't have values numerically above 31
			if ((uint)idx >= 32u)
				throw new InternalStateException($"gamepad button '{btn}' doesn't fit into the 32-bit bitset");
			buttons |= 1u << idx;
		}
		LeftX = leftX;
		LeftY = leftY;
		RightX = rightX;
		RightY = rightY;
		LeftTrigger = leftTrigger;
		RightTrigger = rightTrigger;
	}

	internal GamepadState(uint bitset, float leftX, float leftY, float rightX, float rightY, float leftTrigger, float rightTrigger) {
		buttons = bitset;
		LeftX = leftX;
		LeftY = leftY;
		RightX = rightX;
		RightY = rightY;
		LeftTrigger = leftTrigger;
		RightTrigger = rightTrigger;
	}

	/// <summary>
	/// Whether <paramref name="button"/> is held down.
	/// </summary>
	public bool IsDown(GamepadButton button) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 32u)
			return false;
		return (buttons & 1u << idx) != 0;
	}

	/// <summary>
	/// Gets the value of <paramref name="axis"/>; 0 for <see cref="GamepadAxis.Unknown"/>.
	/// </summary>
	public float GetAxis(GamepadAxis axis) {
		return axis.Tag switch {
			GamepadAxis.Case.LeftX => LeftX,
			GamepadAxis.Case.LeftY => LeftY,
			GamepadAxis.Case.RightX => RightX,
			GamepadAxis.Case.RightY => RightY,
			GamepadAxis.Case.LeftTrigger => LeftTrigger,
			GamepadAxis.Case.RightTrigger => RightTrigger,
			_ => 0f,
		};
	}
}

/// <summary>
/// One connected gamepad's state in a <see cref="GamepadStateSet"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Id"/> is.
/// </remarks>
public readonly record struct GamepadStateEntry(
	GamepadId Id,
	GamepadState State
);

/// <summary>
/// The states of all connected gamepads, in the order they were connected.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is the empty set, same as <see cref="Rest"/>.
/// </remarks>
public readonly struct GamepadStateSet : IReadOnlyList<GamepadStateEntry> {
	private readonly GamepadStateEntry[]? entriesBacking;
	private ReadOnlySpan<GamepadStateEntry> entries => entriesBacking ?? ReadOnlySpan<GamepadStateEntry>.Empty;

	/// <summary>
	/// The empty set.
	/// </summary>
	public static readonly GamepadStateSet Rest = default;

	/// <summary>
	/// The number of gamepads.
	/// </summary>
	public int Count => entries.Length;

	/// <summary>
	/// Gets an entry by index.
	/// </summary>
	public GamepadStateEntry this[int idx] => entries[idx];

	/// <summary>
	/// Creates a set from a copy of <paramref name="gamepads"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="gamepads"/> contains a gamepad ID more than once.
	/// </exception>
	public GamepadStateSet(ReadOnlySpan<GamepadStateEntry> gamepads) {
		entriesBacking = gamepads.ToArray();
		if (entriesBacking.Length != entriesBacking.DistinctBy(static e => e.Id).Count())
			throw new ArgumentException("entry list must not have duplicate gamepad IDs", nameof(gamepads));
	}

	/// <inheritdoc cref="GamepadStateSet(ReadOnlySpan{GamepadStateEntry})"/>
	public GamepadStateSet(params GamepadStateEntry[] gamepads) : this(gamepads.AsSpan()) {
	}

	/// <summary>
	/// Whether the set contains the gamepad <paramref name="gamepadId"/>.
	/// </summary>
	public bool Contains(GamepadId gamepadId) => TryGetState(gamepadId, out _);

	/// <summary>
	/// Gets the state of the gamepad <paramref name="gamepadId"/>, if it is in the set.
	/// </summary>
	/// <param name="gamepadId">Gamepad to look up.</param>
	/// <param name="state">The gamepad's state, or <see cref="GamepadState.Rest"/> if not found.</param>
	public bool TryGetState(GamepadId gamepadId, out GamepadState state) {
		foreach (GamepadStateEntry ent in entries)
			if (ent.Id == gamepadId) {
				state = ent.State;
				return true;
			}
		state = GamepadState.Rest;
		return false;
	}

	/// <summary>
	/// Gets the state of the gamepad <paramref name="gamepadId"/>, or <see cref="GamepadState.Rest"/>
	/// if it is not in the set.
	/// </summary>
	public GamepadState GetStateOrRest(GamepadId gamepadId) => TryGetState(gamepadId, out GamepadState state)
		? state
		: GamepadState.Rest;

	/// <summary>
	/// Returns an enumerator over the entries.
	/// </summary>
	public ReadOnlySpan<GamepadStateEntry>.Enumerator GetEnumerator() => entries.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => (entriesBacking ?? []).GetEnumerator();
	IEnumerator<GamepadStateEntry> IEnumerable<GamepadStateEntry>.GetEnumerator() =>
		((IEnumerable<GamepadStateEntry>)(entriesBacking ?? [])).GetEnumerator();

	/// <summary>
	/// Returns the entries as a span.
	/// </summary>
	public ReadOnlySpan<GamepadStateEntry> AsSpan() => entries;

	public static implicit operator ReadOnlySpan<GamepadStateEntry>(GamepadStateSet s) => s.AsSpan();
}

/// <summary>
/// The pointer's held buttons and position.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is the state before any pointer event: no
/// buttons held, position (0, 0) relative to no window; i.e. same as <see cref="Rest"/>.
/// </remarks>
public readonly struct PointerState {
	private readonly byte buttons;

	/// <summary>
	/// The window <see cref="X"/> and <see cref="Y"/> are relative to, i.e. the window of the last
	/// pointer event; invalid if there hasn't been one, or the source couldn't attribute it to a
	/// window.
	/// </summary>
	public HostWindowId Window { get; }

	/// <summary>
	/// Horizontal position in <see cref="Window"/>'s coordinates.
	/// </summary>
	public float X { get; }

	/// <summary>
	/// Vertical position in <see cref="Window"/>'s coordinates.
	/// </summary>
	public float Y { get; }

	/// <summary>
	/// Whether the pointer is currently inside <see cref="Window"/>.
	/// </summary>
	/// <remarks>
	/// After the pointer moves into another window, this is <see langword="false"/> until that
	/// window's first pointer event, since <see cref="X"/>/<see cref="Y"/> still describe the old
	/// window until then.
	/// </remarks>
	public bool InsideWindow { get; }

	/// <summary>
	/// The state before any pointer event.
	/// </summary>
	public static readonly PointerState Rest = default;

	/// <summary>
	/// Creates a state with exactly the buttons in <paramref name="down"/> held down and the given
	/// position.
	/// </summary>
	public PointerState(ReadOnlySpan<PointerButton> down, HostWindowId window, float x, float y, bool insideWindow) {
		foreach (PointerButton btn in down) {
			int idx = (int)btn.Tag;
			// PointerButton is a [ClosedEnum], and as of right now, doesn't have values numerically above 7
			if ((uint)idx >= 8u)
				throw new InternalStateException($"pointer button '{btn}' doesn't fit into the 8-bit bitset");
			buttons |= (byte)(1u << idx);
		}
		Window = window;
		X = x;
		Y = y;
		InsideWindow = insideWindow;
	}

	internal PointerState(byte bitset, HostWindowId window, float x, float y, bool insideWindow) {
		buttons = bitset;
		Window = window;
		X = x;
		Y = y;
		InsideWindow = insideWindow;
	}

	/// <summary>
	/// Whether <paramref name="button"/> is held down.
	/// </summary>
	public bool IsDown(PointerButton button) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 8u)
			return false;
		return (buttons & 1u << idx) != 0;
	}
}
