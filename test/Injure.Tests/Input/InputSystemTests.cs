// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;
using Injure.Input;

namespace Injure.Tests.Input;

public sealed class InputSystemTests {
	private static List<InputEvent> read(InputSystem input, ref InputCursor cursor) {
		List<InputEvent> result = new();
		foreach (InputEvent ev in input.CreateViewAndAdvance(ref cursor).Events)
			result.Add(ev);
		return result;
	}

	// ==========================================================================
	// cursors and history
	[Fact]
	public static void NewCursorOnlySeesLaterEvents() {
		InputSystem input = new(16);
		input.TryHandle(Ev.Kb(Key.A, true));
		InputCursor c = input.CreateCursor();
		input.TryHandle(Ev.Kb(Key.B, true));
		Assert.Equal(Key.B, Assert.IsType<KeyEvent>(Assert.Single(read(input, ref c))).Key);
		Assert.Empty(read(input, ref c));
	}

	[Fact]
	public static void CursorsAreIndependent() {
		InputSystem input = new(16);
		InputCursor c1 = input.CreateCursor();
		InputCursor c2 = c1;
		input.TryHandle(Ev.Kb(Key.A, true));
		Assert.Single(read(input, ref c1));
		input.TryHandle(Ev.Kb(Key.A, false));
		Assert.Single(read(input, ref c1));
		Assert.Equal(2, read(input, ref c2).Count);
	}

	[Fact]
	public static void CreateViewDoesNotAdvance() {
		InputSystem input = new(16);
		InputCursor c = input.CreateCursor();
		input.TryHandle(Ev.Kb(Key.A, true));
		Assert.Equal(1, input.CreateView(c, out InputCursor next).Events.Count);
		Assert.Equal(1, input.CreateView(c, out _).Events.Count);
		Assert.Equal(0, input.CreateView(next, out _).Events.Count);
	}

	[Fact]
	public static void AdvanceToCurrentSkipsEvents() {
		InputSystem input = new(16);
		InputCursor c = input.CreateCursor();
		input.TryHandle(Ev.Kb(Key.A, true));
		input.AdvanceToCurrent(ref c);
		Assert.Empty(read(input, ref c));
	}

	[Fact]
	public static void ForeignAndDefaultCursorsAreRejected() {
		InputSystem a = new(16);
		InputSystem b = new(16);
		InputCursor fromB = b.CreateCursor();
		Assert.Throws<ArgumentException>(() => a.CreateViewAndAdvance(ref fromB));
		Assert.Throws<ArgumentException>(() => a.CreateView(default, out _));
		InputCursor def = default;
		Assert.Throws<ArgumentException>(() => a.AdvanceToCurrent(ref def));
	}

	[Fact]
	public static void OverflowReportsLostHistoryAndResetsCursor() {
		InputSystem input = new(4);
		InputCursor c = input.CreateCursor();
		for (int i = 0; i < 6; i++)
			input.TryHandle(Ev.PtrMove(i, 0));
		InputView view = input.CreateViewAndAdvance(ref c);
		Assert.True(view.HistoryLost);
		Assert.Equal(2ul, view.LostEventCount);
		Assert.Equal(0, view.Events.Count);
		Assert.Equal(5f, view.State.Pointer.X);
		// the cursor is now at the end, so the next read is clean
		input.TryHandle(Ev.PtrMove(9, 0));
		InputView next = input.CreateViewAndAdvance(ref c);
		Assert.False(next.HistoryLost);
		Assert.Equal(1, next.Events.Count);
	}

	[Fact]
	public static void CursorExactlyAtCapacityLosesNothing() {
		InputSystem input = new(4);
		InputCursor c = input.CreateCursor();
		for (int i = 0; i < 4; i++)
			input.TryHandle(Ev.PtrMove(i, 0));
		Assert.Equal(4, read(input, ref c).Count);
	}

	[Fact]
	public static void CapacityMustBePositive() {
		Assert.Throws<ArgumentOutOfRangeException>(static () => new InputSystem(0));
	}

	// ==========================================================================
	// feeding
	[Fact]
	public static void TryHandleReportsPureInputOnly() {
		InputSystem input = new(16);
		var w = HostWindowId.Allocate();
		Assert.True(input.TryHandle(Ev.Kb(Key.A, true)));
		Assert.True(input.TryHandle(HostEvent.ForText(Ev.Tm, w, "a")));
		Assert.True(input.TryHandle(Ev.PtrMove(1, 1)));
		Assert.True(input.TryHandle(Ev.PadAdded(GamepadId.Allocate())));
		Assert.False(input.TryHandle(HostEvent.Quit(Ev.Tm)));
		Assert.False(input.TryHandle(HostEvent.ForWindow(HostEventKind.WindowShown, Ev.Tm, w)));
		// used for pointer state, but also relevant to window state tracking
		Assert.False(input.TryHandle(HostEvent.ForWindow(HostEventKind.WindowPointerEntered, Ev.Tm, w)));
		Assert.False(input.TryHandle(HostEvent.ForWindow(HostEventKind.WindowPointerLeft, Ev.Tm, w)));
	}

	[Fact]
	public static void KeyRepeatsAndUnknownValuesAreDropped() {
		InputSystem input = new(16);
		InputCursor c = input.CreateCursor();
		var p = GamepadId.Allocate();
		input.TryHandle(Ev.Kb(Key.A, true, repeat: true));
		input.TryHandle(Ev.Kb(Key.Unknown, true));
		input.TryHandle(Ev.PtrButton(PointerButton.Unknown, true));
		input.TryHandle(Ev.PadAxis(p, GamepadAxis.Unknown, 1f));
		input.TryHandle(Ev.PadButton(p, GamepadButton.Unknown, true));
		Assert.Empty(read(input, ref c));
		Assert.False(input.CurrentState.Keyboard.IsDown(Key.A));
	}

	[Fact]
	public static void EventsAreTranslatedFaithfully() {
		InputSystem input = new(16);
		InputCursor c = input.CreateCursor();
		var w = HostWindowId.Allocate();
		var p = GamepadId.Allocate();
		input.TryHandle(HostEvent.ForPointerMove(Ev.Tm, w, new HostPointerMoveEvent(1, 2, 3, 4)));
		input.TryHandle(Ev.PtrButton(PointerButton.X2, true, 5, 6, clicks: 2, window: w));
		input.TryHandle(Ev.Wheel(0.5f, -1f, 0, -1, 7, 8, w));
		input.TryHandle(Ev.PadAdded(p));
		input.TryHandle(Ev.PadAxis(p, GamepadAxis.LeftTrigger, 0.25f));

		List<InputEvent> evs = read(input, ref c);
		Assert.Equal(new PointerMoveEvent(Ev.Tm, w, 1, 2, 3, 4), evs[0]);
		Assert.Equal(new PointerButtonEvent(Ev.Tm, w, PointerButton.X2, EdgeType.Press, 2, 5, 6), evs[1]);
		Assert.Equal(new PointerWheelEvent(Ev.Tm, w, 0.5f, -1f, 0, -1, 7, 8), evs[2]);
		Assert.Equal(new GamepadAddedEvent(Ev.Tm, p), evs[3]);
		Assert.Equal(new GamepadAxisEvent(Ev.Tm, p, GamepadAxis.LeftTrigger, 0.25f), evs[4]);
	}

	// ==========================================================================
	// device state
	[Fact]
	public static void KeyboardAndPointerStateFollowEvents() {
		InputSystem input = new(16);
		input.TryHandle(Ev.Kb(Key.LeftShift, true));
		input.TryHandle(Ev.Kb(Key.W, true));
		input.TryHandle(Ev.Kb(Key.W, false));
		input.TryHandle(Ev.PtrButton(PointerButton.Right, true, 3, 4));
		InputSnapshot s = input.CurrentState;
		Assert.True(s.Keyboard.IsDown(Key.LeftShift));
		Assert.True(s.Keyboard.Shift);
		Assert.False(s.Keyboard.IsDown(Key.W));
		Assert.True(s.Pointer.IsDown(PointerButton.Right));
		Assert.False(s.Pointer.IsDown(PointerButton.Left));
		Assert.Equal((3f, 4f), (s.Pointer.X, s.Pointer.Y));
	}

	[Fact]
	public static void GamepadStateFollowsEvents() {
		InputSystem input = new(32);
		var p1 = GamepadId.Allocate();
		var p2 = GamepadId.Allocate();
		input.TryHandle(Ev.PadAdded(p1));
		input.TryHandle(Ev.PadAdded(p2));
		input.TryHandle(Ev.PadAdded(p1)); // duplicate add is ignored for state
		input.TryHandle(Ev.PadButton(p2, GamepadButton.Start, true));
		input.TryHandle(Ev.PadAxis(p1, GamepadAxis.RightY, -0.5f));

		GamepadStateSet pads = input.CurrentState.Gamepads;
		Assert.Equal([p1, p2], pads.AsSpan().ToArray().Select(static e => e.Id));
		Assert.Equal(-0.5f, pads.GetStateOrRest(p1).RightY);
		Assert.True(pads.GetStateOrRest(p2).IsDown(GamepadButton.Start));

		input.TryHandle(Ev.PadRemoved(p1));
		Assert.False(input.CurrentState.Gamepads.Contains(p1));
		Assert.Single(input.CurrentState.Gamepads);
	}

	[Fact]
	public static void EventsForUnknownGamepadDontCreateState() {
		InputSystem input = new(16);
		var p = GamepadId.Allocate();
		input.TryHandle(Ev.PadButton(p, GamepadButton.South, true));
		Assert.Empty(input.CurrentState.Gamepads);
	}

	[Fact]
	public static void ClearAllDevicesReleasesEverything() {
		InputSystem input = new(32);
		var p = GamepadId.Allocate();
		input.TryHandle(Ev.Kb(Key.Escape, true));
		input.TryHandle(Ev.PtrButton(PointerButton.Middle, true));
		input.TryHandle(Ev.PadAdded(p));
		input.TryHandle(Ev.PadButton(p, GamepadButton.East, true));
		input.TryHandle(Ev.PadAxis(p, GamepadAxis.LeftX, 0.9f));
		InputCursor c = input.CreateCursor();

		input.ClearAllDevices(Ev.Tm);

		List<InputEvent> evs = read(input, ref c);
		Assert.Contains(evs, static e => e is KeyEvent { Key.Tag: Key.Case.Escape, Edge.Tag: EdgeType.Case.Release });
		Assert.Contains(evs, static e => e is PointerButtonEvent { Button.Tag: PointerButton.Case.Middle, Edge.Tag: EdgeType.Case.Release, Clicks: 0 });
		Assert.Contains(evs, static e => e is GamepadButtonEvent { Button.Tag: GamepadButton.Case.East, Edge.Tag: EdgeType.Case.Release });
		Assert.Contains(evs, static e => e is GamepadAxisEvent { Axis.Tag: GamepadAxis.Case.LeftX, Value: 0f });
		InputSnapshot s = input.CurrentState;
		Assert.False(s.Keyboard.IsDown(Key.Escape));
		Assert.False(s.Pointer.IsDown(PointerButton.Middle));
		Assert.Equal(0f, s.Gamepads.GetStateOrRest(p).LeftX);
		Assert.True(s.Gamepads.Contains(p)); // still connected
	}

	[Fact]
	public static void ClearKeyboardAndPointerLeavesGamepadsAlone() {
		InputSystem input = new(32);
		var p = GamepadId.Allocate();
		input.TryHandle(Ev.PadAdded(p));
		input.TryHandle(Ev.PadButton(p, GamepadButton.East, true));
		input.ClearKeyboardAndPointer(Ev.Tm);
		Assert.True(input.CurrentState.Gamepads.GetStateOrRest(p).IsDown(GamepadButton.East));
	}
}
