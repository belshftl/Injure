// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Host;
using Injure.Input;
using static Injure.Tests.Input.Ev;

namespace Injure.Tests.Input;

public sealed class ActionTrackerTests {
	private static readonly InputButtonSource keyA = InputButtonSource.Key(Key.A);
	private static readonly InputButtonSource keyD = InputButtonSource.Key(Key.D);
	private static readonly InputButtonSource keyW = InputButtonSource.Key(Key.W);
	private static readonly InputButtonSource keyS = InputButtonSource.Key(Key.S);

	private static (ActionId, InputRig) buttonRig(params InputButtonSource[] sources) {
		ActionId a = new ActionRegistry().Register("t::button");
		InputRig rig = new(InputRig.Map(b => {
			foreach (InputButtonSource s in sources)
				b.BindButton(a, s);
		}));
		rig.Step();
		return (a, rig);
	}

	private static (ActionId, InputRig) axisRig(Action<ActionMapBuilder, ActionId> bind) {
		ActionId a = new ActionRegistry().Register("t::axis");
		InputRig rig = new(InputRig.Map(b => bind(b, a)));
		rig.Step();
		return (a, rig);
	}

	// ==========================================================================
	// buttons
	[Fact]
	public static void ButtonPressAndReleaseAcrossSteps() {
		(ActionId a, InputRig rig) = buttonRig(keyA);

		InputStep s1 = rig.Feed(Kb(Key.A, true)).Step();
		Assert.Equal(new ButtonActionState(Down: true, PreviousDown: false), s1.Actions.GetButton(a));
		Assert.True(s1.Actions.GetButton(a).Pressed);
		Assert.Equal([EdgeType.Press], s1.ButtonEvents(a).Select(static e => e.Edge));

		InputStep s2 = rig.Step();
		Assert.Equal(new ButtonActionState(true, true), s2.Actions.GetButton(a));
		Assert.Empty(s2.Events);

		InputStep s3 = rig.Feed(Kb(Key.A, false)).Step();
		Assert.True(s3.Actions.GetButton(a).Released);
		Assert.Equal([EdgeType.Release], s3.ButtonEvents(a).Select(static e => e.Edge));
	}

	[Fact]
	public static void PressAndReleaseInOneStepOnlyShowsInEvents() {
		(ActionId a, InputRig rig) = buttonRig(keyA);
		InputStep s = rig.Feed(Kb(Key.A, true), Kb(Key.A, false)).Step();
		Assert.Equal(new ButtonActionState(false, false), s.Actions.GetButton(a));
		Assert.Equal([EdgeType.Press, EdgeType.Release], s.ButtonEvents(a).Select(static e => e.Edge));
	}

	[Fact]
	public static void MultibindPressesPerSourceReleasesOnLast() {
		(ActionId a, InputRig rig) = buttonRig(keyA, keyD);
		Assert.Single(rig.Feed(Kb(Key.A, true)).Step().ButtonEvents(a));
		Assert.Single(rig.Feed(Kb(Key.D, true)).Step().ButtonEvents(a)); // another press
		InputStep s = rig.Feed(Kb(Key.A, false)).Step();
		Assert.Empty(s.ButtonEvents(a));
		Assert.True(s.Actions.GetButton(a).Down);
		Assert.Equal([EdgeType.Release], rig.Feed(Kb(Key.D, false)).Step().ButtonEvents(a).Select(static e => e.Edge));
	}

	[Fact]
	public static void UnboundInputDoesNothing() {
		(ActionId a, InputRig rig) = buttonRig(keyA);
		InputStep s = rig.Feed(Kb(Key.B, true)).Step();
		Assert.False(s.Actions.GetButton(a).Down);
		Assert.Empty(s.Events);
	}

	[Fact]
	public static void InputHeldBeforeFirstUpdateIsPressedOnFirstUpdate() {
		ActionId a = new ActionRegistry().Register("t::button");
		InputRig rig = new(InputRig.Map(b => b.BindButton(a, keyA)));
		rig.Input.TryHandle(Kb(Key.A, true));
		rig.Input.AdvanceToCurrent(ref rig.Cursor); // the consumer never saw the press event
		InputStep s = rig.Step();
		Assert.True(s.Actions.GetButton(a).Down);
		Assert.Equal([EdgeType.Press], s.ButtonEvents(a).Select(static e => e.Edge));
	}

	[Fact]
	public static void KeyActionCarriesFocusedWindow() {
		var w = HostWindowId.Allocate();
		(ActionId a, InputRig rig) = buttonRig(keyA);
		InputStep s = rig.Feed(Kb(Key.A, true, w), Kb(Key.A, false, w)).Step();
		Assert.All(s.ButtonEvents(a), e => {
			Assert.True(e.Info.TryGetKey(out KeyButtonActionInfo info));
			Assert.Equal(w, info.Window);
			Assert.False(e.Info.TryGetPointer(out _));
		});
		Assert.Equal(2, s.ButtonEvents(a).Count());
	}

	[Fact]
	public static void PointerButtonActionCarriesPointerInfo() {
		var w = HostWindowId.Allocate();
		(ActionId a, InputRig rig) = buttonRig(InputButtonSource.PointerButton(PointerButton.Left));
		ButtonActionEvent ev = rig.Feed(PtrButton(PointerButton.Left, true, 10, 20, clicks: 2, window: w)).Step().ButtonEvents(a).Single();
		Assert.True(ev.Info.TryGetPointer(out PointerButtonActionInfo info));
		Assert.Equal(new PointerButtonActionInfo(w, 10, 20, 2), info);

		Assert.Throws<InvalidOperationException>(() => ev.Info.Key);
	}

	[Fact]
	public static void GamepadAndResyncPressesCarryNoInfo() {
		var p = GamepadId.Allocate();
		(ActionId a, InputRig rig) = buttonRig(InputButtonSource.GamepadButton(GamepadButton.South));
		rig.Feed(PadAdded(p), PadButton(p, GamepadButton.South, true));
		Assert.Equal(ButtonActionEventInfoKind.None, rig.Step().ButtonEvents(a).Single().Info.Kind);

		// a press re-derived from device state has no source event to take info from
		ActionId k = new ActionRegistry().Register("t::key");
		InputRig keyRig = new(InputRig.Map(b => b.BindButton(k, keyA)));
		keyRig.Input.TryHandle(Kb(Key.A, true, HostWindowId.Allocate()));
		keyRig.Input.AdvanceToCurrent(ref keyRig.Cursor);
		Assert.Equal(ButtonActionEventInfoKind.None, keyRig.Step().ButtonEvents(k).Single().Info.Kind);
	}

	// ==========================================================================
	// 1D state axes
	[Fact]
	public static void StateAxisReportsValueDeltaAndChangeEvents() {
		var p = GamepadId.Allocate();
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindStateAxis(a, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftX), AxisDeadzone.None));
		rig.Feed(PadAdded(p)).Step();

		InputStep s1 = rig.Feed(PadAxis(p, GamepadAxis.LeftX, 0.5f)).Step();
		Assert.Equal(new StateAxisActionState(0.5f, 0f), s1.Actions.GetStateAxis(a));
		Assert.Equal(0.5f, s1.Actions.GetStateAxis(a).StepDelta);
		Assert.Equal(0.5f, Assert.IsType<StateAxisActionEvent>(Assert.Single(s1.Events)).Value);

		InputStep s2 = rig.Step(); // unchanged: no event
		Assert.Empty(s2.Events);
		Assert.Equal(new StateAxisActionState(0.5f, 0.5f), s2.Actions.GetStateAxis(a));
	}

	[Fact]
	public static void FirstUpdateEmitsEveryStateAxis() {
		ActionId a = new ActionRegistry().Register("t::axis");
		InputRig rig = new(InputRig.Map(b => b.BindStateAxis(a, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftX), AxisDeadzone.None)));
		Assert.Equal(0f, Assert.IsType<StateAxisActionEvent>(Assert.Single(rig.Step().Events)).Value);
	}

	[Fact]
	public static void StateAxisReadsMostDeflectedGamepad() {
		var p1 = GamepadId.Allocate();
		var p2 = GamepadId.Allocate();
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindStateAxis(a, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftY), AxisDeadzone.None));
		rig.Feed(PadAdded(p1), PadAdded(p2), PadAxis(p1, GamepadAxis.LeftY, 0.3f), PadAxis(p2, GamepadAxis.LeftY, -0.6f));
		Assert.Equal(-0.6f, rig.Step().Actions.GetStateAxis(a).Value);
	}

	[Theory]
	[InlineData(SocdPolicy.Case.Last, 1f)]
	[InlineData(SocdPolicy.Case.First, -1f)]
	[InlineData(SocdPolicy.Case.Neutral, 0f)]
	[InlineData(SocdPolicy.Case.Positive, 1f)]
	[InlineData(SocdPolicy.Case.Negative, -1f)]
	public static void DigitalAxisSocdWorks(SocdPolicy.Case policy, float expectedBothHeld) {
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindStateAxis(a, InputStateAxisSource.DigitalPair(keyA, keyD, SocdPolicy.Enum.FromTag(policy)), AxisDeadzone.None));
		Assert.Equal(-1f, rig.Feed(Kb(Key.A, true)).Step().Actions.GetStateAxis(a).Value);
		Assert.Equal(expectedBothHeld, rig.Feed(Kb(Key.D, true)).Step().Actions.GetStateAxis(a).Value);
		Assert.Equal(-1f, rig.Feed(Kb(Key.D, false)).Step().Actions.GetStateAxis(a).Value);
		Assert.Equal(0f, rig.Feed(Kb(Key.A, false)).Step().Actions.GetStateAxis(a).Value);
	}

	[Fact]
	public static void SocdLastFollowsMostRecentPress() {
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindStateAxis(a, InputStateAxisSource.DigitalPair(keyA, keyD), AxisDeadzone.None));
		rig.Feed(Kb(Key.D, true), Kb(Key.A, true));
		Assert.Equal(-1f, rig.Step().Actions.GetStateAxis(a).Value);
	}

	[Theory]
	[InlineData(StateAxisMergePolicy.Case.MaxAbs, -1f)]
	[InlineData(StateAxisMergePolicy.Case.SumClamp, -0.7f)]
	public static void StateAxisMergeWorks(StateAxisMergePolicy.Case policy, float expected) {
		var p = GamepadId.Allocate();
		(ActionId a, InputRig rig) = axisRig((b, a) => {
			b.StateAxisMergePolicy = StateAxisMergePolicy.Enum.FromTag(policy);
			b.BindStateAxis(a, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftX), AxisDeadzone.None);
			b.BindStateAxis(a, InputStateAxisSource.DigitalPair(keyA, keyD), AxisDeadzone.None);
		});
		rig.Feed(PadAdded(p), PadAxis(p, GamepadAxis.LeftX, 0.3f), Kb(Key.A, true));
		Near(expected, rig.Step().Actions.GetStateAxis(a).Value);
	}

	// ==========================================================================
	// 2D state axes
	[Fact]
	public static void DigitalButtonsUseUpIsNegativeYAndNormalizeDiagonals() {
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindStateAxis2d(a, InputStateAxis2dSource.DigitalButtons(keyA, keyD, keyW, keyS), Axis2dDeadzone.None));
		Assert.Equal(new Vector2(0, -1), rig.Feed(Kb(Key.W, true)).Step().Actions.GetStateAxis2d(a).Value);
		Near(new Vector2(MathF.Sqrt(0.5f), -MathF.Sqrt(0.5f)), rig.Feed(Kb(Key.D, true)).Step().Actions.GetStateAxis2d(a).Value);
	}

	[Fact]
	public static void StickReadsMostDeflectedGamepad() {
		var p1 = GamepadId.Allocate();
		var p2 = GamepadId.Allocate();
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindStateAxis2d(a, InputStateAxis2dSource.GamepadStick(GamepadStick.Left), Axis2dDeadzone.None));
		rig.Feed(PadAdded(p1), PadAdded(p2), PadAxis(p1, GamepadAxis.LeftX, 0.3f), PadAxis(p2, GamepadAxis.LeftY, 0.8f));
		Assert.Equal(new Vector2(0, 0.8f), rig.Step().Actions.GetStateAxis2d(a).Value);
	}

	[Fact]
	public static void PairCombinesTwo1dSourcesAndClamps() {
		var p = GamepadId.Allocate();
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindStateAxis2d(
			a,
			InputStateAxis2dSource.Pair(InputStateAxisSource.GamepadAxis(GamepadAxis.RightX), InputStateAxisSource.DigitalPair(keyW, keyS)),
			Axis2dDeadzone.None
		));
		rig.Feed(PadAdded(p), PadAxis(p, GamepadAxis.RightX, 0.5f), Kb(Key.S, true));
		Vector2 v = rig.Step().Actions.GetStateAxis2d(a).Value;
		Near(1f, v.Length());
		Near(0.5f, v.X / v.Y);
	}

	[Fact]
	public static void StateAxis2dScaleAndClampWorks() {
		var p = GamepadId.Allocate();
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindStateAxis2d(a, InputStateAxis2dSource.GamepadStick(GamepadStick.Left), Axis2dDeadzone.None, new Vector2(2, 1)));
		rig.Feed(PadAdded(p), PadAxis(p, GamepadAxis.LeftX, 0.3f));
		Near(new Vector2(0.6f, 0), rig.Step().Actions.GetStateAxis2d(a).Value);
		rig.Feed(PadAxis(p, GamepadAxis.LeftX, 0.8f));
		Near(new Vector2(1, 0), rig.Step().Actions.GetStateAxis2d(a).Value);
	}

	[Fact]
	public static void StateAxis2dMergeWorks() {
		static Vector2 merged(StateAxis2dMergePolicy policy) {
			var p = GamepadId.Allocate();
			(ActionId a, InputRig rig) = axisRig((b, a) => {
				b.StateAxis2dMergePolicy = policy;
				b.BindStateAxis2d(a, InputStateAxis2dSource.GamepadStick(GamepadStick.Left), Axis2dDeadzone.None);
				b.BindStateAxis2d(a, InputStateAxis2dSource.DigitalButtons(keyA, keyD, keyW, keyS), Axis2dDeadzone.None);
			});
			// stick (0.5, 0), digital (0, -1)
			rig.Feed(PadAdded(p), PadAxis(p, GamepadAxis.LeftX, 0.5f), Kb(Key.W, true));
			return rig.Step().Actions.GetStateAxis2d(a).Value;
		}
		Near(new Vector2(0, -1), merged(StateAxis2dMergePolicy.MaxMagnitude));
		Near(new Vector2(0.5f, -1f) / MathF.Sqrt(1.25f), merged(StateAxis2dMergePolicy.SumClamp));
	}

	// ==========================================================================
	// impulse axes and pass-through events
	[Fact]
	public static void ImpulseAxisAccumulatesScaledAmountsWithinStep() {
		var w = HostWindowId.Allocate();
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindImpulseAxis(a, InputImpulseAxisSource.PointerWheel(PointerWheelAxis.Y), 2f));
		InputStep s = rig.Feed(Wheel(0, 1.5f, iy: 1, px: 4, py: 5, window: w), Wheel(0, 1.5f, iy: 2, px: 4, py: 5, window: w)).Step();
		Assert.Equal(6f, s.Actions.GetImpulseAxis(a).Amount);
		var evs = s.Events.OfType<ImpulseAxisActionEvent>().ToList();
		Assert.Equal([3f, 3f], evs.Select(static e => e.Amount));
		Assert.Equal(new PointerImpulseAxisActionInfo(w, 4, 5, 2), evs[1].Info.Pointer);
		// impulses don't carry over
		Assert.Equal(0f, rig.Step().Actions.GetImpulseAxis(a).Amount);
	}

	[Fact]
	public static void WheelOnOtherDimensionIsIgnored() {
		(ActionId a, InputRig rig) = axisRig((b, a) => b.BindImpulseAxis(a, InputImpulseAxisSource.PointerWheel(PointerWheelAxis.Y)));
		InputStep s = rig.Feed(Wheel(1f, 0f, ix: 1)).Step();
		Assert.Empty(s.Events);
		Assert.Equal(0f, s.Actions.GetImpulseAxis(a).Amount);
	}

	[Fact]
	public static void PointerMovesAndTextPassThroughWithoutBindings() {
		var w = HostWindowId.Allocate();
		InputRig rig = new(new ActionMapBuilder().ToSnapshot());
		rig.Step();
		InputStep s = rig.Feed(PtrMove(7, 8, w), HostEvent.ForText(Tm, w, "hi")).Step();
		PointerMoveControlEvent move = Assert.IsType<PointerMoveControlEvent>(s.Events[0]);
		Assert.Equal((w, 7f, 8f), (move.Window, move.X, move.Y));
		TextEnteredControlEvent text = Assert.IsType<TextEnteredControlEvent>(s.Events[1]);
		Assert.Equal((w, "hi"), (text.Window, text.Text));
		Assert.Equal(w, s.RawPointer.Window);
	}

	[Fact]
	public static void RawStateIsExposed() {
		var p = GamepadId.Allocate();
		InputRig rig = new(new ActionMapBuilder().ToSnapshot());
		InputStep s = rig.Feed(Kb(Key.F5, true), PadAdded(p), PadButton(p, GamepadButton.Back, true)).Step();
		Assert.True(s.RawKeyboard.IsDown(Key.F5));
		Assert.True(s.RawGamepads.GetStateOrRest(p).IsDown(GamepadButton.Back));
	}

	// ==========================================================================
	// map changes
	[Fact]
	public static void ReplacingTheMapReleasesAndRebaselines() {
		ActionId a = new ActionRegistry().Register("t::button");
		InputRig rig = new(InputRig.Map(b => b.BindButton(a, keyA)));
		Assert.True(rig.Feed(Kb(Key.A, true)).Step().Actions.GetButton(a).Down);

		// rebind to a key that isn't held: released
		rig.Profile.Replace(InputRig.Map(b => b.BindButton(a, keyD)));
		InputStep s1 = rig.Step();
		Assert.Equal([EdgeType.Release], s1.ButtonEvents(a).Select(e => e.Edge));
		Assert.False(s1.Actions.GetButton(a).Down);

		// rebind back to the key that's still held: pressed without a new key event
		rig.Profile.Replace(InputRig.Map(b => b.BindButton(a, keyA)));
		InputStep s2 = rig.Step();
		Assert.Equal([EdgeType.Press], s2.ButtonEvents(a).Select(e => e.Edge));
		Assert.True(s2.Actions.GetButton(a).Down);
	}

	[Fact]
	public static void ReplacingTheMapResetsStateAxesWithOneEventEach() {
		var p = GamepadId.Allocate();
		ActionRegistry reg = new();
		ActionId a1 = reg.Register("t::axis");
		ActionId a2 = reg.Register("t::axis2d");
		InputRig rig = new(InputRig.Map(b => {
			b.BindStateAxis(a1, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftX), AxisDeadzone.None);
			b.BindStateAxis2d(a2, InputStateAxis2dSource.GamepadStick(GamepadStick.Right), Axis2dDeadzone.None);
		}));
		rig.Feed(PadAdded(p), PadAxis(p, GamepadAxis.LeftX, 0.5f), PadAxis(p, GamepadAxis.RightY, -0.5f));
		Assert.Equal(0.5f, rig.Step().Actions.GetStateAxis(a1).Value);

		rig.Profile.Replace(new ActionMapBuilder().ToSnapshot());
		InputStep s = rig.Step();
		Assert.Equal(new StateAxisActionState(0f, 0.5f), s.Actions.GetStateAxis(a1));
		Assert.Equal(new StateAxis2dActionState(Vector2.Zero, new Vector2(0, -0.5f)), s.Actions.GetStateAxis2d(a2));
		Assert.Equal(0f, Assert.Single(s.Events.OfType<StateAxisActionEvent>()).Value);
		Assert.Equal(Vector2.Zero, Assert.Single(s.Events.OfType<StateAxis2dActionEvent>()).Value);
	}

	[Fact]
	public static void ResyncEmitsStillBoundAxisOnceWithItsCurrentValue() {
		var p = GamepadId.Allocate();
		ActionId a = new ActionRegistry().Register("t::axis");
		InputRig rig = new(InputRig.Map(b => b.BindStateAxis(a, InputStateAxisSource.GamepadAxis(GamepadAxis.LeftX), AxisDeadzone.None)), capacity: 8);
		rig.Feed(PadAdded(p), PadAxis(p, GamepadAxis.LeftX, 0.5f));
		rig.Step();

		for (int i = 0; i < 20; i++)
			rig.Feed(PtrMove(i, i)); // overflow the history
		InputStep s = rig.Step();
		Assert.Equal(0.5f, Assert.Single(s.Events.OfType<StateAxisActionEvent>()).Value);
		Assert.Equal(new StateAxisActionState(0.5f, 0.5f), s.Actions.GetStateAxis(a));
	}
}
