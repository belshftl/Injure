// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;

namespace Injure.Input;

public abstract record InputEvent(HostTick Tick);

public sealed record KeyEvent(
	HostTick Tick,
	HostWindowId Window, // window that had keyboard focus / pointer; invalid if unknown
	Key Key,
	EdgeType Edge
) : InputEvent(Tick);

public sealed record GamepadAddedEvent(
	HostTick Tick,
	GamepadId Gamepad
) : InputEvent(Tick);

public sealed record GamepadRemovedEvent(
	HostTick Tick,
	GamepadId Gamepad
) : InputEvent(Tick);

public sealed record GamepadAxisEvent(
	HostTick Tick,
	GamepadId Gamepad,
	GamepadAxis Axis,
	float Value // [-1, 1] for sticks, [0, 1] for triggers
) : InputEvent(Tick);

public sealed record GamepadButtonEvent(
	HostTick Tick,
	GamepadId Gamepad,
	GamepadButton Button,
	EdgeType Edge
) : InputEvent(Tick);

public sealed record PointerMoveEvent(
	HostTick Tick,
	HostWindowId Window, // window that had keyboard focus / pointer; invalid if unknown
	float X,
	float Y,
	float DeltaX,
	float DeltaY
) : InputEvent(Tick);

public sealed record PointerButtonEvent(
	HostTick Tick,
	HostWindowId Window, // window that had keyboard focus / pointer; invalid if unknown
	PointerButton Button,
	EdgeType Edge,
	int Clicks,
	float X,
	float Y
) : InputEvent(Tick);

public sealed record PointerWheelEvent(
	HostTick Tick,
	HostWindowId Window, // window that had keyboard focus / pointer; invalid if unknown
	float X,
	float Y,
	int IntegerX,
	int IntegerY,
	float MouseX,
	float MouseY
) : InputEvent(Tick);

public sealed record TextEnteredEvent(
	HostTick Tick,
	HostWindowId Window, // window that had keyboard focus / pointer; invalid if unknown
	string Text
) : InputEvent(Tick);
