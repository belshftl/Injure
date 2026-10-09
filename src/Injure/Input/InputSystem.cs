// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Injure.Collections;
using Injure.Host;

namespace Injure.Input;

/// <summary>
/// The standard <see cref="IInputSource"/>: turns <see cref="HostEvent"/>s into raw input events
/// and device state.
/// </summary>
/// <remarks>
/// <para>
/// Fed by passing every host event to <see cref="TryHandle(in HostEvent)"/>; a <c>StandardGame</c>
/// does this on its own, and a custom loop has to do it manually.
/// </para>
/// <para>
/// Keyboard state is shared by all windows, since only one window has keyboard focus at a time.
/// Pointer state remembers which window its coordinates are relative to
/// (<see cref="PointerState.Window"/>). Gamepads are tracked individually by
/// <see cref="GamepadId"/>.
/// </para>
/// <para>
/// Key repeats and input values without an engine equivalent (<c>Unknown</c> keys, buttons,
/// and axes) are dropped and never become input events.
/// </para>
/// <para>
/// Not thread-safe; use it from the thread that feeds it.
/// </para>
/// </remarks>
public sealed class InputSystem : IInputSource {
	/// <remarks>
	/// The <see langword="default"/> value is valid and is an all-released, centered gamepad with
	/// an invalid ID.
	/// </remarks>
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
	/// <summary>
	/// The window <see cref="pointerX"/>/<see cref="pointerY"/> are relative to.
	/// </summary>
	private HostWindowId pointerWindow;
	private float pointerX;
	private float pointerY;
	/// <summary>
	/// The window the pointer is currently inside, per the last enter/leave events.
	/// </summary>
	/// <remarks>
	/// Tracked separately from <see cref="pointerWindow"/>, and only cleared by a leave event for the
	/// same window, so that entering window B before the leave event for window A arrives can't end
	/// up as "outside".
	/// </remarks>
	private HostWindowId pointerInside;

	/// <summary>
	/// The window of the last keyboard event; synthesized key releases are attributed to it.
	/// </summary>
	private HostWindowId keyboardWindow;

	private readonly List<MutableGamepadState> gamepads = new();

	/// <summary>
	/// Creates an input system with no input and an empty event history.
	/// </summary>
	/// <param name="maxBufferedEvents">
	/// Capacity of the event history; a cursor that falls further behind than this loses history.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="maxBufferedEvents"/> is not positive.
	/// </exception>
	public InputSystem(int maxBufferedEvents) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBufferedEvents);
		events = new Ring<InputEvent>(maxBufferedEvents);
	}

	/// <inheritdoc/>
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
		pointerInside.IsValid && pointerInside == pointerWindow
	);

	private ulong oldestSeq => nextSeq - checked((ulong)events.Count);

	// ==========================================================================
	// IInputSource

	/// <inheritdoc/>
	public InputCursor CreateCursor() => new(cursorSourceToken, nextSeq);

	/// <inheritdoc/>
	public InputView CreateViewAndAdvance(ref InputCursor cursor) {
		InputView view = CreateView(cursor, out InputCursor next);
		cursor = next;
		return view;
	}

	/// <inheritdoc/>
	public InputView CreateView(InputCursor cursor, out InputCursor next) {
		validateCursor(cursor);

		ulong currentOldestSeq = oldestSeq;
		ulong currentNextSeq = nextSeq;

		// cursors are only ever created at or before nextSeq, and nextSeq never decreases
		if (cursor.Seq > currentNextSeq)
			throw new InternalStateException("input cursor points beyond the current event history");

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

	/// <inheritdoc/>
	public void AdvanceToCurrent(ref InputCursor cursor) {
		validateCursor(cursor);
		cursor = new InputCursor(cursorSourceToken, nextSeq);
	}

	private void validateCursor(InputCursor cursor) {
		if (!cursor.BelongsTo(cursorSourceToken))
			throw new ArgumentException("input cursor was not created by this input source", nameof(cursor));
	}

	// ==========================================================================
	// feed

	/// <summary>
	/// Updates input state from <paramref name="ev"/> and records the corresponding input events,
	/// if any.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if <paramref name="ev"/> is purely an input event (keyboard, text,
	/// pointer, or gamepad), so other handlers can ignore it; <see langword="false"/> for every
	/// other event, including <see cref="HostEventKind.WindowPointerEntered"/> and
	/// <see cref="HostEventKind.WindowPointerLeft"/>, which this uses but which are also relevant
	/// to window state tracking.
	/// </returns>
	/// <remarks>
	/// Removing a gamepad first records releases for its pressed buttons and resets its deflected
	/// axes to 0, so consumers never see input stay active on a gamepad that is gone.
	/// </remarks>
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
			return false;
		case HostEventKind.Case.WindowPointerLeft:
			if (pointerInside == ev.Window)
				pointerInside = default;
			return false;
		case HostEventKind.Case.GamepadAdded:
			Push(new GamepadAddedEvent(ev.Tick, ev.Gamepad));
			return true;
		case HostEventKind.Case.GamepadRemoved:
			if (tryFindGamepad(ev.Gamepad, out int idx))
				synthesizeGamepadRelease(ev.Tick, gamepads[idx]);
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

	/// <summary>
	/// Applies <paramref name="ev"/> to the device state and records it in the event history.
	/// </summary>
	/// <remarks>
	/// Internal until custom input sources are designed, which is meant to be the public way of
	/// feeding arbitrary input events.
	/// </remarks>
	internal void Push(InputEvent ev) {
		ArgumentNullException.ThrowIfNull(ev);
		applyEventToState(ev);
		events.PushNewest(ev);
		checked {
			nextSeq++;
		}
	}

	// ==========================================================================
	// reset

	/// <summary>
	/// Discards the event history while keeping device state.
	/// </summary>
	/// <remarks>
	/// Every cursor that was not already at the end of the history gets a view with
	/// <see cref="InputView.HistoryLost"/> set on its next read.
	/// </remarks>
	public void DiscardHistory() => events.Clear();

	/// <summary>
	/// Records releases for every pressed key and pointer button, e.g. so that input held while a
	/// menu opens stops counting.
	/// </summary>
	/// <param name="tick">Timestamp for the recorded release events.</param>
	/// <remarks>
	/// The releases are attributed to the window of the last keyboard or pointer event respectively.
	/// </remarks>
	public void ClearKeyboardAndPointer(HostTick tick) {
		synthesizeKeyboardReleaseAll(tick);
		synthesizePointerReleaseAll(tick);
		Debug.Assert(keys0 == 0 && keys1 == 0 && keys2 == 0 && keys3 == 0 && pointerButtons == 0);
	}

	/// <summary>
	/// Like <see cref="ClearKeyboardAndPointer(HostTick)"/>, but also records releases for every
	/// pressed gamepad button and resets every deflected gamepad axis to 0.
	/// </summary>
	/// <param name="tick">Timestamp for the recorded events.</param>
	public void ClearAllDevices(HostTick tick) {
		ClearKeyboardAndPointer(tick);
		for (int i = 0; i < gamepads.Count; i++)
			synthesizeGamepadRelease(tick, gamepads[i]);
	}

	// ==========================================================================
	// state tracking
	private void applyEventToState(InputEvent ev) {
		switch (ev) {
		case KeyEvent keyEv:
			setKey(keyEv.Key, keyEv.Edge == EdgeType.Press);
			keyboardWindow = keyEv.Window;
			break;
		case PointerMoveEvent pmoveEv:
			setPointerPosition(pmoveEv.Window, pmoveEv.X, pmoveEv.Y);
			break;
		case PointerButtonEvent pbtnEv:
			setPointerButton(pbtnEv.Button, pbtnEv.Edge == EdgeType.Press);
			setPointerPosition(pbtnEv.Window, pbtnEv.X, pbtnEv.Y);
			break;
		case PointerWheelEvent pwheelEv:
			setPointerPosition(pwheelEv.Window, pwheelEv.PointerX, pwheelEv.PointerY);
			break;
		case GamepadAddedEvent gaddEv:
			if (!tryFindGamepad(gaddEv.Gamepad, out _))
				gamepads.Add(new MutableGamepadState { Id = gaddEv.Gamepad });
			break;
		case GamepadRemovedEvent gremEv:
			if (tryFindGamepad(gremEv.Gamepad, out int idxRem))
				gamepads.RemoveAt(idxRem);
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

	private void setPointerPosition(HostWindowId window, float x, float y) {
		pointerWindow = window;
		pointerX = x;
		pointerY = y;
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
			if (down)
				keys0 |= mask;
			else
				keys0 &= ~mask;
			break;
		case 1:
			if (down)
				keys1 |= mask;
			else
				keys1 &= ~mask;
			break;
		case 2:
			if (down)
				keys2 |= mask;
			else
				keys2 &= ~mask;
			break;
		case 3:
			if (down)
				keys3 |= mask;
			else
				keys3 &= ~mask;
			break;
		default:
			throw new UnreachableException();
		}
	}

	private void setPointerButton(PointerButton button, bool down) {
		int idx = (int)button.Tag;
		if ((uint)idx >= 8u)
			return;
		byte mask = (byte)(1u << idx);
		if (down)
			pointerButtons |= mask;
		else
			pointerButtons &= (byte)~mask;
	}

	private static void setGamepadAxis(ref MutableGamepadState g, GamepadAxis axis, float value) {
		switch (axis.Tag) {
		case GamepadAxis.Case.LeftX:
			g.LeftX = value;
			break;
		case GamepadAxis.Case.LeftY:
			g.LeftY = value;
			break;
		case GamepadAxis.Case.RightX:
			g.RightX = value;
			break;
		case GamepadAxis.Case.RightY:
			g.RightY = value;
			break;
		case GamepadAxis.Case.LeftTrigger:
			g.LeftTrigger = value;
			break;
		case GamepadAxis.Case.RightTrigger:
			g.RightTrigger = value;
			break;
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

	// ==========================================================================
	// synthesized events
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

	/// <summary>
	/// Records releases for every pressed button of <paramref name="g"/> and resets its deflected axes
	/// to 0.
	/// </summary>
	/// <remarks>
	/// Takes the state by value on purpose: the pushed events modify the gamepad's list entry while
	/// this iterates over the original state.
	/// </remarks>
	private void synthesizeGamepadRelease(HostTick tick, MutableGamepadState g) {
		for (int bit = 0; bit < 32; bit++) {
			if ((g.Buttons & 1u << bit) == 0)
				continue;
			Push(new GamepadButtonEvent(tick, g.Id, GamepadButton.Enum.FromTag((GamepadButton.Case)bit), EdgeType.Release));
		}
		if (g.LeftX != 0f)
			Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.LeftX, 0f));
		if (g.LeftY != 0f)
			Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.LeftY, 0f));
		if (g.RightX != 0f)
			Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.RightX, 0f));
		if (g.RightY != 0f)
			Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.RightY, 0f));
		if (g.LeftTrigger != 0f)
			Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.LeftTrigger, 0f));
		if (g.RightTrigger != 0f)
			Push(new GamepadAxisEvent(tick, g.Id, GamepadAxis.RightTrigger, 0f));
	}
}
