// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;
using Injure.Input;

namespace Injure.Tests.Host;

public sealed class HostEventTests {
	private static readonly HostTick t = HostTick.DangerousCreateFromRaw(1234);

	[Fact]
	public static void PayloadsRoundtrip() {
		var w = HostWindowId.Allocate();
		var g = GamepadId.Allocate();

		var moved = HostEvent.WindowMoved(t, w, -5, 7);
		Assert.Equal(HostEventKind.WindowMoved, moved.Kind);
		Assert.Equal((-5, 7), moved.Position);
		Assert.Equal(w, moved.Window);
		Assert.Equal(t, moved.Tick);

		Assert.Equal((640, 480), HostEvent.WindowPixelSizeChanged(t, w, 640, 480).Size);
		Assert.Equal(1.5f, HostEvent.WindowDisplayScaleChanged(t, w, 1.5f).Scale);

		HostKeyEvent key = new(Key.A, EdgeType.Press, Repeat: true);
		Assert.Equal(key, HostEvent.ForKey(t, w, key).Key);

		HostPointerButtonEvent btn = new(PointerButton.Right, EdgeType.Release, 2, 1.25f, -3f);
		Assert.Equal(btn, HostEvent.ForPointerButton(t, default, btn).PointerButton);

		HostPointerWheelEvent wheel = new(0.5f, -1f, 0, -1, 10f, 20f);
		Assert.Equal(wheel, HostEvent.ForPointerWheel(t, w, wheel).PointerWheel);

		HostGamepadAxisEvent axis = new(g, GamepadAxis.RightTrigger, 0.75f);
		var axisEv = HostEvent.ForGamepadAxis(t, axis);
		Assert.Equal(axis, axisEv.GamepadAxis);
		Assert.False(axisEv.Window.IsValid);

		Assert.Equal(g, HostEvent.GamepadRemoved(t, g).Gamepad);
		Assert.Equal("héllo", HostEvent.ForText(t, w, "héllo").Text);
	}

	[Fact]
	public static void WrongPayloadAccessThrows() {
		var ev = HostEvent.WindowResized(t, HostWindowId.Allocate(), 1, 2);
		Assert.Throws<InvalidOperationException>(() => ev.Position);
		Assert.Throws<InvalidOperationException>(() => ev.Key);
		Assert.Throws<InvalidOperationException>(() => ev.Text);
		Assert.Throws<InvalidOperationException>(() => HostEvent.Quit(t).Gamepad);
	}

	[Fact]
	public static void WindowEventsRequireValidWindow() {
		Assert.Throws<ArgumentException>(() => HostEvent.ForWindow(HostEventKind.WindowShown, t, default));
		Assert.Throws<ArgumentException>(() => HostEvent.WindowMoved(t, default, 0, 0));
		// payload-carrying kinds can't go through the payload-less factory
		Assert.Throws<ArgumentException>(() => HostEvent.ForWindow(HostEventKind.WindowMoved, t, HostWindowId.Allocate()));
		Assert.Throws<ArgumentException>(() => HostEvent.ForWindow(HostEventKind.Key, t, HostWindowId.Allocate()));
	}
}
