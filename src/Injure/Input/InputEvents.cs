// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;

namespace Injure.Input;

/// <summary>
/// A raw input event recorded by an <see cref="IInputSource"/>.
/// </summary>
/// <param name="Tick">When the event happened.</param>
/// <remarks>
/// Keyboard, text, and pointer events carry the window they belong to (the window with keyboard
/// focus, or the window the pointer coordinates are relative to); it is invalid if the event
/// couldn't be attributed to a window. Gamepad events belong to no window.
/// </remarks>
public abstract record InputEvent(HostTick Tick);

/// <summary>
/// A key was pressed or released. Never a key repeat.
/// </summary>
public sealed record KeyEvent(
	HostTick Tick,
	HostWindowId Window,
	Key Key,
	EdgeType Edge
) : InputEvent(Tick);

/// <summary>
/// A gamepad was connected.
/// </summary>
public sealed record GamepadAddedEvent(
	HostTick Tick,
	GamepadId Gamepad
) : InputEvent(Tick);

/// <summary>
/// A gamepad was disconnected. Releases of everything it held are recorded before this.
/// </summary>
public sealed record GamepadRemovedEvent(
	HostTick Tick,
	GamepadId Gamepad
) : InputEvent(Tick);

/// <summary>
/// A gamepad axis moved. <see cref="Value"/> is in [-1, 1] for stick axes (+Y down) and [0, 1] for
/// triggers.
/// </summary>
public sealed record GamepadAxisEvent(
	HostTick Tick,
	GamepadId Gamepad,
	GamepadAxis Axis,
	float Value
) : InputEvent(Tick);

/// <summary>
/// A gamepad button was pressed or released.
/// </summary>
public sealed record GamepadButtonEvent(
	HostTick Tick,
	GamepadId Gamepad,
	GamepadButton Button,
	EdgeType Edge
) : InputEvent(Tick);

/// <summary>
/// The pointer moved, to (<see cref="X"/>, <see cref="Y"/>) in <see cref="Window"/>'s coordinates.
/// </summary>
public sealed record PointerMoveEvent(
	HostTick Tick,
	HostWindowId Window,
	float X,
	float Y,
	float DeltaX,
	float DeltaY
) : InputEvent(Tick);

/// <summary>
/// A pointer button was pressed or released, at (<see cref="X"/>, <see cref="Y"/>) in
/// <see cref="Window"/>'s coordinates.
/// </summary>
/// <remarks>
/// For a press, <see cref="Clicks"/> is 1 for a single click, 2 for a double click, and so on; it
/// is 0 for synthesized releases.
/// </remarks>
public sealed record PointerButtonEvent(
	HostTick Tick,
	HostWindowId Window,
	PointerButton Button,
	EdgeType Edge,
	int Clicks,
	float X,
	float Y
) : InputEvent(Tick);

/// <summary>
/// The pointer wheel scrolled, with the pointer at (<see cref="PointerX"/>, <see cref="PointerY"/>)
/// in <see cref="Window"/>'s coordinates.
/// </summary>
/// <remarks>
/// Positive <see cref="Y"/> scrolls away from the user and positive <see cref="X"/> to the right,
/// regardless of "natural scrolling" settings. <see cref="IntegerX"/>/<see cref="IntegerY"/> are
/// whole wheel notches, accumulated by the platform from fractional scrolling.
/// </remarks>
public sealed record PointerWheelEvent(
	HostTick Tick,
	HostWindowId Window,
	float X,
	float Y,
	int IntegerX,
	int IntegerY,
	float PointerX,
	float PointerY
) : InputEvent(Tick);

/// <summary>
/// Text was entered.
/// </summary>
/// <remarks>
/// A stopgap until there is a proper text input API; this is not suitable for text editing (no IME
/// composition).
/// </remarks>
public sealed record TextEnteredEvent(
	HostTick Tick,
	HostWindowId Window,
	string Text
) : InputEvent(Tick);
