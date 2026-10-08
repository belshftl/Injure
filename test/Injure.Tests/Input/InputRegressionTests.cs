// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;
using Injure.Input;

namespace Injure.Tests.Input;

public sealed class InputRegressionTests {
	[Fact]
	public static void StateAxisDeadzoneIsApplied() {
		ActionId a = new ActionRegistry().Register("t::axis");
		InputRig rig = new(InputRig.Map(b => b.BindStateAxis(a, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftX), AxisDeadzone.Threshold(0.2f))));
		var p = GamepadId.Allocate();
		rig.Feed(Ev.PadAdded(p), Ev.PadAxis(p, GamepadAxis.LeftX, 0.1f));
		Assert.Equal(0f, rig.Step().Actions.GetStateAxis(a).Value);
		rig.Feed(Ev.PadAxis(p, GamepadAxis.LeftX, 0.5f));
		Assert.Equal(0.5f, rig.Step().Actions.GetStateAxis(a).Value);
	}

	[Fact]
	public static void StateAxisIsClampedAfterScaling() {
		ActionId a = new ActionRegistry().Register("t::axis");
		InputRig rig = new(InputRig.Map(b => b.BindStateAxis(a, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftX), AxisDeadzone.None, scale: 3f)));
		var p = GamepadId.Allocate();
		rig.Feed(Ev.PadAdded(p), Ev.PadAxis(p, GamepadAxis.LeftX, -0.5f));
		Assert.Equal(-1f, rig.Step().Actions.GetStateAxis(a).Value);
	}

	[Fact]
	public static void SameButtonOnTwoGamepadsStaysHeldUntilBothRelease() {
		ActionId a = new ActionRegistry().Register("t::button");
		InputRig rig = new(InputRig.Map(b => b.BindButton(a, InputButtonSource.GamepadButton(GamepadButton.South))));
		var p1 = GamepadId.Allocate();
		var p2 = GamepadId.Allocate();
		rig.Feed(Ev.PadAdded(p1), Ev.PadAdded(p2)).Step();

		rig.Feed(Ev.PadButton(p1, GamepadButton.South, true), Ev.PadButton(p2, GamepadButton.South, true));
		Assert.True(rig.Step().Actions.GetButton(a).Pressed);

		rig.Feed(Ev.PadButton(p1, GamepadButton.South, false));
		InputStep step = rig.Step();
		Assert.True(step.Actions.GetButton(a).Down);
		Assert.Empty(step.ButtonEvents(a));

		rig.Feed(Ev.PadButton(p2, GamepadButton.South, false));
		Assert.True(rig.Step().Actions.GetButton(a).Released);
	}

	[Fact]
	public static void RemovingGamepadReleasesWhatItHeld() {
		ActionId a = new ActionRegistry().Register("t::button");
		InputRig rig = new(InputRig.Map(b => b.BindButton(a, InputButtonSource.GamepadButton(GamepadButton.North))));
		var p = GamepadId.Allocate();
		InputCursor raw = rig.Input.CreateCursor();
		rig.Feed(Ev.PadAdded(p), Ev.PadButton(p, GamepadButton.North, true), Ev.PadAxis(p, GamepadAxis.RightTrigger, 0.7f));
		Assert.True(rig.Step().Actions.GetButton(a).Down);

		rig.Feed(Ev.PadRemoved(p));
		Assert.True(rig.Step().Actions.GetButton(a).Released);

		List<InputEvent> evs = new();
		foreach (InputEvent ev in rig.Input.CreateViewAndAdvance(ref raw).Events)
			evs.Add(ev);
		int removedAt = evs.FindIndex(e => e is GamepadRemovedEvent);
		Assert.Contains(evs[..removedAt], e => e is GamepadButtonEvent { Button.Tag: GamepadButton.Case.North, Edge.Tag: EdgeType.Case.Release });
		Assert.Contains(evs[..removedAt], e => e is GamepadAxisEvent { Axis.Tag: GamepadAxis.Case.RightTrigger, Value: 0f });
	}

	[Fact]
	public static void HistoryLossResyncsFromCurrentState() {
		ActionId a = new ActionRegistry().Register("t::button");
		InputRig rig = new(InputRig.Map(b => b.BindButton(a, InputButtonSource.Key(Key.Space))), capacity: 64);
		rig.Feed(Ev.Kb(Key.Space, true));
		Assert.True(rig.Step().Actions.GetButton(a).Down);

		// space released and pressed again while the consumer wasn't looking, then history overflows
		rig.Feed(Ev.Kb(Key.Space, false), Ev.Kb(Key.Space, true));
		for (int i = 0; i < 100; i++)
			rig.Feed(Ev.PtrMove(i, i));

		InputStep step = rig.Step(); // used to throw NotImplementedException
		Assert.True(step.Actions.GetButton(a).Down);
		// release of the old hold, then a press re-derived from the device state
		Assert.Equal([EdgeType.Release, EdgeType.Press], step.ButtonEvents(a).Select(e => e.Edge));
	}

	[Fact]
	public static void DiscardHistoryMakesCursorsReportLoss() {
		InputSystem input = new(16);
		InputCursor behind = input.CreateCursor();
		input.TryHandle(Ev.PtrMove(1, 2));
		InputCursor current = input.CreateCursor();
		input.DiscardHistory();
		InputView lost = input.CreateViewAndAdvance(ref behind);
		Assert.True(lost.HistoryLost);
		Assert.Equal(1UL, lost.LostEventCount);
		Assert.False(input.CreateViewAndAdvance(ref current).HistoryLost);
		Assert.Equal(1f, input.CurrentState.Pointer.X); // state is kept
	}

	[Fact]
	public static void WheelUpdatesPointerPosition() {
		var w = HostWindowId.Allocate();
		InputSystem input = new(16);
		input.TryHandle(Ev.Wheel(0, 1, iy: 1, px: 30, py: 40, window: w));
		PointerState ptr = input.CurrentState.Pointer;
		Assert.Equal((w, 30f, 40f), (ptr.Window, ptr.X, ptr.Y));
	}

	[Fact]
	public static void ClearKeyboardAndPointerDoesNotThrowOnTagGaps() {
		InputSystem input = new(16);
		input.TryHandle(Ev.Kb(Key.Application, true));
		input.ClearKeyboardAndPointer(Ev.Tm); // used to throw on the first undeclared Key tag
		Assert.False(input.CurrentState.Keyboard.IsDown(Key.Application));
	}

	[Fact]
	public static void ActionIdsFromDifferentRegistriesDiffer() {
		ActionRegistry r1 = new();
		ActionRegistry r2 = new();
		ActionId a1 = r1.Register("game::jump");
		ActionId a2 = r2.Register("game::jump");
		Assert.NotEqual(a1, a2);
		Assert.Throws<ArgumentException>(() => r1.GetSid(a2));
		Assert.False(r1.TryGetSid(a2, out _));
		Assert.Equal("game::jump", r2.GetSid(a2));
	}

	[Fact]
	public static void DefaultViewsAreEmpty() {
		ControlView view = default;
		ActionId id = new ActionRegistry().Register("game::x");
		Assert.Equal(default, view.Actions.Buttons[id]);
		Assert.Equal(default, ActionStateView.Empty.ImpulseAxes[id]);
		Assert.Empty(default(ActionStateView).ToSnapshot().Buttons);
	}
}
