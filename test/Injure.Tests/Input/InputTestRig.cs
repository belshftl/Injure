// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;
using Injure.Input;

namespace Injure.Tests.Input;

internal sealed record InputStep(
	ActionStateSnapshot Actions,
	List<ControlEvent> Events,
	KeyboardState RawKeyboard,
	PointerState RawPointer,
	GamepadStateSet RawGamepads
) {
	public IEnumerable<ButtonActionEvent> ButtonEvents(ActionId action) =>
		Events.OfType<ButtonActionEvent>().Where(e => e.Action == action);
}

internal sealed class InputRig {
	public readonly InputSystem Input;
	public readonly ActionProfile Profile;
	public readonly ActionTracker Tracker;
	public InputCursor Cursor;

	public InputRig(ActionMapSnapshot map, int capacity = 256) {
		Input = new InputSystem(capacity);
		Profile = new ActionProfile(map);
		Tracker = new ActionTracker(Profile);
		Cursor = Input.CreateCursor();
	}

	public InputRig Feed(params HostEvent[] events) {
		foreach (HostEvent ev in events)
			Input.TryHandle(ev);
		return this;
	}

	public InputStep Step() {
		ControlView view = Tracker.Update(Ev.Tm, Input.CreateViewAndAdvance(ref Cursor));
		return new InputStep(view.Actions.ToSnapshot(), view.Events.ToArray().ToList(), view.RawKeyboard, view.RawPointer, view.RawGamepads);
	}

	public static ActionMapSnapshot Map(Action<ActionMapBuilder> bind) {
		ActionMapBuilder b = new();
		bind(b);
		return b.ToSnapshot();
	}
}

internal static class Ev {
	public static readonly HostTick Tm = HostTick.DangerousCreateFromRaw(1_000);

	public static HostEvent Kb(Key key, bool down, HostWindowId window = default, bool repeat = false) =>
		HostEvent.ForKey(Tm, window, new HostKeyEvent(key, down ? EdgeType.Press : EdgeType.Release, repeat));

	public static HostEvent PtrButton(PointerButton button, bool down, float x = 0, float y = 0, int clicks = 1, HostWindowId window = default) =>
		HostEvent.ForPointerButton(Tm, window, new HostPointerButtonEvent(button, down ? EdgeType.Press : EdgeType.Release, clicks, x, y));

	public static HostEvent PtrMove(float x, float y, HostWindowId window = default) =>
		HostEvent.ForPointerMove(Tm, window, new HostPointerMoveEvent(x, y, 0, 0));

	public static HostEvent Wheel(float x, float y, int ix = 0, int iy = 0, float px = 0, float py = 0, HostWindowId window = default) =>
		HostEvent.ForPointerWheel(Tm, window, new HostPointerWheelEvent(x, y, ix, iy, px, py));

	public static HostEvent PadAdded(GamepadId id) => HostEvent.GamepadAdded(Tm, id);
	public static HostEvent PadRemoved(GamepadId id) => HostEvent.GamepadRemoved(Tm, id);

	public static HostEvent PadButton(GamepadId id, GamepadButton button, bool down) =>
		HostEvent.ForGamepadButton(Tm, new HostGamepadButtonEvent(id, button, down ? EdgeType.Press : EdgeType.Release));

	public static HostEvent PadAxis(GamepadId id, GamepadAxis axis, float value) =>
		HostEvent.ForGamepadAxis(Tm, new HostGamepadAxisEvent(id, axis, value));

	public static void Near(float expected, float actual) => Assert.Equal(expected, actual, 1e-4);

	public static void Near(System.Numerics.Vector2 expected, System.Numerics.Vector2 actual) {
		Near(expected.X, actual.X);
		Near(expected.Y, actual.Y);
	}
}
