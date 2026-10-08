// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;
using Injure.Input;

namespace Injure.Tests.Input;

public sealed class InputWindowAttributionTests {
	private static readonly HostTick t = HostTick.DangerousCreateFromRaw(1000);

	private static List<InputEvent> drain(InputSystem input, ref InputCursor cursor) {
		List<InputEvent> result = new();
		foreach (InputEvent ev in input.CreateViewAndAdvance(ref cursor).Events)
			result.Add(ev);
		return result;
	}

	[Fact]
	public static void EventsCarryTheirWindow() {
		var w1 = HostWindowId.Allocate();
		var w2 = HostWindowId.Allocate();
		InputSystem input = new(64);
		InputCursor cursor = input.CreateCursor();

		input.TryHandle(HostEvent.ForKey(t, w1, new HostKeyEvent(Key.A, EdgeType.Press, Repeat: false)));
		input.TryHandle(HostEvent.ForPointerMove(t, w2, new HostPointerMoveEvent(10, 20, 1, 2)));
		input.TryHandle(HostEvent.ForText(t, w1, "x"));
		input.TryHandle(HostEvent.ForPointerButton(t, w2, new HostPointerButtonEvent(PointerButton.Left, EdgeType.Press, 1, 10, 20)));

		List<InputEvent> evs = drain(input, ref cursor);
		Assert.Equal(w1, Assert.IsType<KeyEvent>(evs[0]).Window);
		Assert.Equal(w2, Assert.IsType<PointerMoveEvent>(evs[1]).Window);
		Assert.Equal(w1, Assert.IsType<TextEnteredEvent>(evs[2]).Window);
		Assert.Equal(w2, Assert.IsType<PointerButtonEvent>(evs[3]).Window);
	}

	[Fact]
	public static void PointerStateTracksItsWindow() {
		var w1 = HostWindowId.Allocate();
		var w2 = HostWindowId.Allocate();
		InputSystem input = new(64);
		Assert.False(input.CurrentState.Pointer.Window.IsValid);

		input.TryHandle(HostEvent.ForWindow(HostEventKind.WindowPointerEntered, t, w1));
		input.TryHandle(HostEvent.ForPointerMove(t, w1, new HostPointerMoveEvent(5, 6, 0, 0)));
		Assert.Equal(w1, input.CurrentState.Pointer.Window);
		Assert.True(input.CurrentState.Pointer.InsideWindow);

		// entering w2 before leaving w1 must not end up as "outside"
		input.TryHandle(HostEvent.ForWindow(HostEventKind.WindowPointerEntered, t, w2));
		input.TryHandle(HostEvent.ForWindow(HostEventKind.WindowPointerLeft, t, w1));
		// coordinates still refer to w1 until a w2 pointer event arrives
		Assert.Equal(w1, input.CurrentState.Pointer.Window);
		Assert.False(input.CurrentState.Pointer.InsideWindow);

		input.TryHandle(HostEvent.ForPointerMove(t, w2, new HostPointerMoveEvent(7, 8, 0, 0)));
		Assert.Equal(w2, input.CurrentState.Pointer.Window);
		Assert.True(input.CurrentState.Pointer.InsideWindow);

		input.TryHandle(HostEvent.ForWindow(HostEventKind.WindowPointerLeft, t, w2));
		Assert.False(input.CurrentState.Pointer.InsideWindow);
	}

	[Fact]
	public static void SynthesizedReleasesUseLastWindows() {
		var kw = HostWindowId.Allocate();
		var pw = HostWindowId.Allocate();
		InputSystem input = new(64);
		input.TryHandle(HostEvent.ForKey(t, kw, new HostKeyEvent(Key.B, EdgeType.Press, Repeat: false)));
		input.TryHandle(HostEvent.ForPointerButton(t, pw, new HostPointerButtonEvent(PointerButton.Right, EdgeType.Press, 1, 3, 4)));
		InputCursor cursor = input.CreateCursor();

		input.ClearKeyboardAndPointer(t);

		List<InputEvent> evs = drain(input, ref cursor);
		KeyEvent key = Assert.Single(evs.OfType<KeyEvent>());
		Assert.Equal((kw, EdgeType.Release), (key.Window, key.Edge));
		PointerButtonEvent btn = Assert.Single(evs.OfType<PointerButtonEvent>());
		Assert.Equal((pw, EdgeType.Release), (btn.Window, btn.Edge));
	}
}
