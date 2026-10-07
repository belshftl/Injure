// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Injure.Collections;
using Injure.Host;

namespace Injure.Input;

internal sealed class InputSystem : IInputSource {
	private struct MutableGamepadState {
		public GamepadId Id;
		public uint Buttons;
		public float LeftX;
		public float LeftY;
		public float RightX;
		public float RightY;
		public float LeftTrigger;
		public float RightTrigger;
	}

	private readonly object cursorSourceToken = new();
	private readonly Ring<InputEvent> events;

	private ulong nextSeq = 0;

	private ulong keys0;
	private ulong keys1;
	private ulong keys2;
	private ulong keys3;

	private byte pointerButtons;
	private HostWindowId pointerWindow; // window pointerX/pointerY are relative to
	private float pointerX;
	private float pointerY;
	private HostWindowId pointerInside; // tracked separately so enter-B-before-leave-A orderings stay right
	private bool pointerCaptured;
	private HostWindowId keyboardWindow; // window of the last keyboard event

	private readonly List<MutableGamepadState> gamepads = new();

	public InputSystem(int maxBufferedEvents) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBufferedEvents);
		events = new Ring<InputEvent>(maxBufferedEvents);
	}

	public InputSnapshot CurrentState => new(
		new KeyboardState(keys0, keys1, keys2, keys3),
		currentPointerState,
		snapshotGamepads()
	);

	private PointerState currentPointerState => new(
		pointerButtons,
		pointerWindow,
		pointerX,
		pointerY,
		pointerInside.IsValid && pointerInside == pointerWindow,
		pointerCaptured
	);

	private ulong oldestSeq => nextSeq - checked((ulong)events.Count);

	public InputCursor CreateCursor() => new(cursorSourceToken, nextSeq);

	public InputView CreateViewSince(ref InputCursor cursor) {
		InputView view = CreateViewSince(cursor, out InputCursor next);
		cursor = next;
		return view;
	}

	public InputView CreateViewSince(InputCursor cursor, out InputCursor next) {
		validateCursor(cursor);

		ulong currentOldestSeq = oldestSeq;
		ulong currentNextSeq = nextSeq;

		if (cursor.Seq > currentNextSeq)
			throw new ArgumentOutOfRangeException(nameof(cursor), "input cursor points beyond the current event history");

		next = new InputCursor(cursorSourceToken, currentNextSeq);

		if (cursor.Seq < currentOldestSeq) {
			ulong lostCount = currentOldestSeq - cursor.Seq;
			return new InputView(
				InputEventView.Empty, // don't return the retained tail
				CurrentState,
				historyLost: true,
				lostEventCount: lostCount
			);
		}

		int start = checked((int)(cursor.Seq - currentOldestSeq));
		int length = checked((int)(currentNextSeq - cursor.Seq));

		return new InputView(
			new InputEventView(events.View(start, length)),
			CurrentState,
			historyLost: false,
			lostEventCount: 0
		);
	}

	public void AdvanceToCurrent(ref InputCursor cursor) {
		validateCursor(cursor);
		cursor = new InputCursor(cursorSourceToken, nextSeq);
	}

	private void validateCursor(InputCursor cursor) {
		if (!cursor.BelongsTo(cursorSourceToken))
			throw new ArgumentException("input cursor was not created by this input source", nameof(cursor));
	}

	public void Push(InputEvent ev) {
		ArgumentNullException.ThrowIfNull(ev);
		applyEventToState(ev);
		events.PushNewest(ev);
		checked {
			nextSeq++;
		}
	}

	public void SetPointerCaptured(bool captured) => pointerCaptured = captured;

	public void DiscardAll() {
		events.Clear();
	}

	public void ClearKeyboardAndPointer(HostTick tick) {
		synthesizeKeyboardReleaseAll(tick);
		synthesizePointerReleaseAll(tick);
		keys0 = keys1 = keys2 = keys3 = 0;
		pointerButtons = 0;
	}

	public void ClearAllDevices(HostTick tick) {
		synthesizeKeyboardReleaseAll(tick);
		synthesizePointerReleaseAll(tick);
		synthesizeGamepadsReleaseAll(tick);
		keys0 = keys1 = keys2 = keys3 = 0;
		pointerButtons = 0;
		for (int i = 0; i < gamepads.Count; i++) {
			MutableGamepadState g = gamepads[i];
			g.Buttons = 0;
			g.LeftX = g.LeftY = g.RightX = g.RightY = g.LeftTrigger = g.RightTrigger = 0f;
			gamepads[i] = g;
		}
	}

	private void applyEventToState(InputEvent ev) {
		switch (ev) {
		case KeyEvent keyEv:
			setKey(keyEv.Key, keyEv.Edge == EdgeType.Press);
			keyboardWindow = keyEv.Window;
			break;
		case PointerMoveEvent pmoveEv:
			pointerWindow = pmoveEv.Window;
			pointerX = pmoveEv.X;
			pointerY = pmoveEv.Y;
			break;
		case PointerButtonEvent pbtnEv:
			setPointerButton(pbtnEv.Button, pbtnEv.Edge == EdgeType.Press);
			pointerWindow = pbtnEv.Window;
			pointerX = pbtnEv.X;
			pointerY = pbtnEv.Y;
			break;
		case GamepadAddedEvent gaddEv:
			if (!tryFindGamepad(gaddEv.Gamepad, out _))
				gamepads.Add(new MutableGamepadState { Id = gaddEv.Gamepad });
			break;
		case GamepadRemovedEvent gremEv:
			removeGamepad(gremEv.Gamepad);
			break;
		case GamepadAxisEvent gaxisEv:
			if (tryFindGamepad(gaxisEv.Gamepad, out int idxAxis)) {
				MutableGamepadState g = gamepads[idxAxis];
				setGamepadAxis(ref g, gaxisEv.Axis, gaxisEv.Value);
				gamepads[idxAxis] = g;
			}
			break;
		case GamepadButtonEvent gbtnEv:
			if (tryFindGamepad(gbtnEv.Gamepad, out int idxBtn)) {
				MutableGamepadState g = gamepads[idxBtn];
				setGamepadButton(ref g, gbtnEv.Button, gbtnEv.Edge == EdgeType.Press);
				gamepads[idxBtn] = g;
			}
			break;
		}
	}

	private bool tryFindGamepad(GamepadId id, out int idx) {
		for (int i = 0; i < gamepads.Count; i++) {
			if (gamepads[i].Id != id)
				continue;
			idx = i;
			return true;
		}
		idx = -1;
		return false;
	}

	private void removeGamepad(GamepadId id) {
		for (int i = 0; i < gamepads.Count; i++) {
			if (gamepads[i].Id != id)
				continue;
			gamepads.RemoveAt(i);
			return;
		}
	}

	private GamepadStateSet snapshotGamepads() {
		if (gamepads.Count == 0)
			return GamepadStateSet.Rest;
		var arr = new GamepadStateEntry[gamepads.Count];
		for (int i = 0; i < gamepads.Count; i++) {
			MutableGamepadState g = gamepads[i];
			arr[i] = new GamepadStateEntry(
				g.Id,
				new GamepadState(g.Buttons, g.LeftX, g.LeftY, g.RightX, g.RightY, g.LeftTrigger, g.RightTrigger)
			);
		}
		return new GamepadStateSet(arr);
	}

	private void setKey(Key key, bool down) {
		int idx = (int)key.Tag;
		if ((uint)idx > 0xffu)
			return;
		int word = idx >> 6;
		ulong mask = 1ul << (idx & 0b111111);
		switch (word) {
		case 0:
			if (down) keys0 |= mask;
			else keys0 &= ~mask;
			break;
		case 1:
			if (down) keys1 |= mask;
			else keys1 &= ~mask;
			break;
		case 2:
			if (down) keys2 |= mask;
			else keys2 &= ~mask;
			break;
		case 3:
			if (down) keys3 |= mask;
			else keys3 &= ~mask;
			break;
		default: throw new UnreachableException();
		}
	}

	private void setPointerButton(PointerButton button, bool down) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 8u)
			return;
		byte mask = (byte)(1u << idx);
		if (down) pointerButtons |= mask;
		else pointerButtons &= (byte)~mask;
	}

	private static void setGamepadAxis(ref MutableGamepadState g, GamepadAxis axis, float value) {
		switch (axis.Tag) {
		case GamepadAxis.Case.LeftX: g.LeftX = value; break;
		case GamepadAxis.Case.LeftY: g.LeftY = value; break;
		case GamepadAxis.Case.RightX: g.RightX = value; break;
		case GamepadAxis.Case.RightY: g.RightY = value; break;
		case GamepadAxis.Case.LeftTrigger: g.LeftTrigger = value; break;
		case GamepadAxis.Case.RightTrigger: g.RightTrigger = value; break;
		}
	}

	private static void setGamepadButton(ref MutableGamepadState g, GamepadButton button, bool down) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 32u)
			return;
		uint mask = 1u << idx;
		if (down) g.Buttons |= mask;
		else g.Buttons &= ~mask;
	}

	private void synthesizeKeyboardReleaseAll(HostTick tick) {
		// declared values only; the raw tag range has gaps
		foreach (Key key in Key.Enum.Values) {
			if (!new KeyboardState(keys0, keys1, keys2, keys3).IsDown(key))
				continue;
			Push(new KeyEvent(tick, keyboardWindow, key, EdgeType.Release));
		}
	}

	private void synthesizePointerReleaseAll(HostTick tick) {
		foreach (PointerButton btn in PointerButton.Enum.Values) {
			if (!currentPointerState.IsDown(btn))
				continue;
			Push(new PointerButtonEvent(tick, pointerWindow, btn, EdgeType.Release, Clicks: 0, pointerX, pointerY));
		}
	}

	private void synthesizeGamepadsReleaseAll(HostTick tick) {
		for (int i = 0; i < gamepads.Count; i++) {
			MutableGamepadState g = gamepads[i];
			for (int bit = 0; bit < 32; bit++) {
				if ((g.Buttons & 1u << bit) == 0)
					continue;
				Push(new GamepadButtonEvent(tick, g.Id, GamepadButton.Enum.FromTag((GamepadButton.Case)bit), EdgeType.Release));
			}
			if (g.LeftX != 0f) Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.LeftX, 0f));
			if (g.LeftY != 0f) Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.LeftY, 0f));
			if (g.RightX != 0f) Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.RightX, 0f));
			if (g.RightY != 0f) Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.RightY, 0f));
			if (g.LeftTrigger != 0f) Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.LeftTrigger, 0f));
			if (g.RightTrigger != 0f) Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.RightTrigger, 0f));
		}
	}

	// keyboard state is shared by all windows since only one has keyboard focus at a time; pointer
	// state remembers which window its coordinates are relative to
	public bool TryHandle(in HostEvent ev) {
		switch (ev.Kind.Tag) {
		case HostEventKind.Case.Key: {
			HostKeyEvent k = ev.Key;
			if (!k.Repeat && k.Key != Key.Unknown)
				Push(new KeyEvent(ev.Tick, ev.Window, k.Key, k.Edge));
			return true;
		}
		case HostEventKind.Case.TextInput:
			Push(new TextEnteredEvent(ev.Tick, ev.Window, ev.Text));
			return true;
		case HostEventKind.Case.PointerMove: {
			HostPointerMoveEvent m = ev.PointerMove;
			Push(new PointerMoveEvent(ev.Tick, ev.Window, m.X, m.Y, m.DeltaX, m.DeltaY));
			return true;
		}
		case HostEventKind.Case.PointerButton: {
			HostPointerButtonEvent b = ev.PointerButton;
			if (b.Button != PointerButton.Unknown)
				Push(new PointerButtonEvent(ev.Tick, ev.Window, b.Button, b.Edge, b.Clicks, b.X, b.Y));
			return true;
		}
		case HostEventKind.Case.PointerWheel: {
			HostPointerWheelEvent w = ev.PointerWheel;
			Push(new PointerWheelEvent(ev.Tick, ev.Window, w.X, w.Y, w.IntegerX, w.IntegerY, w.PointerX, w.PointerY));
			return true;
		}
		case HostEventKind.Case.WindowPointerEntered:
			pointerInside = ev.Window;
			return false; // also of interest to window state tracking
		case HostEventKind.Case.WindowPointerLeft:
			if (pointerInside == ev.Window)
				pointerInside = default;
			return false;
		case HostEventKind.Case.GamepadAdded:
			Push(new GamepadAddedEvent(ev.Tick, ev.Gamepad));
			return true;
		case HostEventKind.Case.GamepadRemoved:
			Push(new GamepadRemovedEvent(ev.Tick, ev.Gamepad));
			return true;
		case HostEventKind.Case.GamepadAxis: {
			HostGamepadAxisEvent a = ev.GamepadAxis;
			if (a.Axis != GamepadAxis.Unknown)
				Push(new GamepadAxisEvent(ev.Tick, a.Gamepad, a.Axis, a.Value));
			return true;
		}
		case HostEventKind.Case.GamepadButton: {
			HostGamepadButtonEvent b = ev.GamepadButton;
			if (b.Button != GamepadButton.Unknown)
				Push(new GamepadButtonEvent(ev.Tick, b.Gamepad, b.Button, b.Edge));
			return true;
		}
		default:
			return false;
		}
	}
}
