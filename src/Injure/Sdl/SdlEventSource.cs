// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using Hexa.NET.SDL3;
using Injure.Host;
using Injure.Input;
using static Injure.Sdl.SdlInputTranslation;

namespace Injure.Sdl;

/// <summary>
/// <see cref="IHostEventSource"/> for SDL's event queue.
/// </summary>
/// <remarks>
/// <para>
/// Obtained from <see cref="SdlContext.Events"/>. Everything except <see cref="Wake"/> and
/// <see cref="Clock"/> is bound to the context's thread, like the rest of <c>Injure.Sdl</c>.
/// </para>
/// <para>
/// SDL events that have no <see cref="HostEvent"/> equivalent, and window events for windows that
/// weren't created through <see cref="SdlWindow"/>, are skipped by <see cref="TryPoll"/>. To see
/// them, poll with <see cref="DangerousGetNextRaw"/> and translate the rest with
/// <see cref="DangerousCreateHostEvent"/>.
/// </para>
/// <para>
/// Gamepads are opened when SDL reports them and closed when they are removed, so gamepad events
/// arrive as long as <see cref="SdlInitOptions.Gamepad"/> was set.
/// </para>
/// </remarks>
public sealed unsafe class SdlEventSource : IHostEventSource {
	private readonly SdlContext context;
	private readonly uint wakeEventType;
	private readonly Dictionary<int, (GamepadId Id, nint Handle)> gamepads = new(); // by SDL_JoystickID
	private int wakePending = 0;
	private int shutDown = 0;

	internal SdlEventSource(SdlContext context) {
		this.context = context;
		wakeEventType = SDL.RegisterEvents(1);
		if (wakeEventType == 0)
			throw SdlException.FromLastError("SDL_RegisterEvents");
	}

	/// <inheritdoc/>
	public IHostClock Clock => context.Clock;

	/// <inheritdoc/>
	/// <remarks>
	/// One millisecond, the resolution of <c>SDL_WaitEventTimeout</c>.
	/// </remarks>
	public HostDuration WaitGranularity { get; } = HostDuration.FromMs(1);

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the context.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the context has been disposed.
	/// </exception>
	public bool TryPoll(out HostEvent ev) {
		while (DangerousGetNextRaw() is SDLEvent raw)
			if (DangerousCreateHostEvent(in raw, out ev))
				return true;
		ev = default;
		return false;
	}

	/// <summary>
	/// Removes and returns the next pending raw SDL event, if there is one. Never blocks.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the context.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the context has been disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Returns <see langword="null"/> if no event is pending. Events used internally for
	/// <see cref="Wake"/> are filtered out. Every returned event <b>must</b> be passed to
	/// <see cref="DangerousCreateHostEvent"/> exactly once, even ones the caller handles itself,
	/// since translation also keeps track of gamepads.
	/// </para>
	/// <para>
	/// The return type is not a stable API; see <c>docs/conventions/dangerous-get.md</c>.
	/// </para>
	/// </remarks>
	public SDLEvent? DangerousGetNextRaw() {
		context.CheckAccess();
		SDLEvent e;
		while (SDL.PollEvent(&e)) {
			if (e.Type == wakeEventType) {
				Volatile.Write(ref wakePending, 0);
				continue;
			}
			return e;
		}
		return null;
	}

	/// <summary>
	/// Translates a raw SDL event obtained from <see cref="DangerousGetNextRaw"/>.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> if the event has no <see cref="HostEvent"/> equivalent.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the context.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the context has been disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Stateful: gamepad IDs are assigned and gamepads opened/closed here, so each event must be
	/// translated exactly once.
	/// </para>
	/// <para>
	/// The parameter type of <paramref name="ev"/> is not a stable API; see
	/// <c>docs/conventions/dangerous-get.md</c>.
	/// </para>
	/// </remarks>
	public bool DangerousCreateHostEvent(in SDLEvent ev, out HostEvent result) {
		if (!translate(in ev, out result, out SdlWindow? owner))
			return false;
		owner?.ApplyEvent(in result);
		return true;
	}

	// owner is set for Window* events, whose window state needs updating
	private bool translate(in SDLEvent ev, out HostEvent result, out SdlWindow? owner) {
		context.CheckAccess();
		owner = null;
		HostTick tick = context.Clock.FromSdlTimestamp(ev.Common.Timestamp);
		switch ((SDLEventType)ev.Type) {
		case SDLEventType.Quit:
			result = HostEvent.Quit(tick);
			return true;

		case SDLEventType.WindowCloseRequested: return window(HostEventKind.WindowCloseRequested, in ev, tick, out result, out owner);
		case SDLEventType.WindowShown: return window(HostEventKind.WindowShown, in ev, tick, out result, out owner);
		case SDLEventType.WindowHidden: return window(HostEventKind.WindowHidden, in ev, tick, out result, out owner);
		case SDLEventType.WindowExposed: return window(HostEventKind.WindowExposed, in ev, tick, out result, out owner);
		case SDLEventType.WindowMinimized: return window(HostEventKind.WindowMinimized, in ev, tick, out result, out owner);
		case SDLEventType.WindowMaximized: return window(HostEventKind.WindowMaximized, in ev, tick, out result, out owner);
		case SDLEventType.WindowRestored: return window(HostEventKind.WindowRestored, in ev, tick, out result, out owner);
		case SDLEventType.WindowEnterFullscreen: return window(HostEventKind.WindowEnteredFullscreen, in ev, tick, out result, out owner);
		case SDLEventType.WindowLeaveFullscreen: return window(HostEventKind.WindowLeftFullscreen, in ev, tick, out result, out owner);
		case SDLEventType.WindowFocusGained: return window(HostEventKind.WindowFocusGained, in ev, tick, out result, out owner);
		case SDLEventType.WindowFocusLost: return window(HostEventKind.WindowFocusLost, in ev, tick, out result, out owner);
		case SDLEventType.WindowMouseEnter: return window(HostEventKind.WindowPointerEntered, in ev, tick, out result, out owner);
		case SDLEventType.WindowMouseLeave: return window(HostEventKind.WindowPointerLeft, in ev, tick, out result, out owner);
		case SDLEventType.WindowMoved:
			if (!context.TryGetWindow(ev.Window.WindowID, out SdlWindow? moved))
				break;
			owner = moved;
			result = HostEvent.WindowMoved(tick, moved.Id, ev.Window.Data1, ev.Window.Data2);
			return true;
		case SDLEventType.WindowResized:
			if (!context.TryGetWindow(ev.Window.WindowID, out SdlWindow? resized))
				break;
			owner = resized;
			result = HostEvent.WindowResized(tick, resized.Id, ev.Window.Data1, ev.Window.Data2);
			return true;
		case SDLEventType.WindowPixelSizeChanged:
			if (!context.TryGetWindow(ev.Window.WindowID, out SdlWindow? pxResized))
				break;
			owner = pxResized;
			result = HostEvent.WindowPixelSizeChanged(tick, pxResized.Id, ev.Window.Data1, ev.Window.Data2);
			return true;
		case SDLEventType.WindowDisplayScaleChanged:
			if (!context.TryGetWindow(ev.Window.WindowID, out SdlWindow? rescaled))
				break;
			owner = rescaled;
			result = HostEvent.WindowDisplayScaleChanged(tick, rescaled.Id, SDL.GetWindowDisplayScale(rescaled.DangerousGetHandle()));
			return true;

		case SDLEventType.KeyDown:
		case SDLEventType.KeyUp:
			result = HostEvent.ForKey(
				tick,
				windowOrInvalid(ev.Key.WindowID),
				new HostKeyEvent(TranslateScancode(ev.Key.Scancode), edge(ev.Key.Down), ev.Key.Repeat != 0)
			);
			return true;
		case SDLEventType.TextInput:
			string? text = Marshal.PtrToStringUTF8((nint)ev.Text.Text);
			if (text is null)
				break;
			result = HostEvent.ForText(tick, windowOrInvalid(ev.Text.WindowID), text);
			return true;
		case SDLEventType.MouseMotion:
			result = HostEvent.ForPointerMove(
				tick,
				windowOrInvalid(ev.Motion.WindowID),
				new HostPointerMoveEvent(ev.Motion.X, ev.Motion.Y, ev.Motion.Xrel, ev.Motion.Yrel)
			);
			return true;
		case SDLEventType.MouseButtonDown:
		case SDLEventType.MouseButtonUp:
			result = HostEvent.ForPointerButton(
				tick,
				windowOrInvalid(ev.Button.WindowID),
				new HostPointerButtonEvent(TranslatePointerButton(ev.Button.Button), edge(ev.Button.Down), ev.Button.Clicks, ev.Button.X, ev.Button.Y)
			);
			return true;
		case SDLEventType.MouseWheel: {
			float x = ev.Wheel.X;
			float y = ev.Wheel.Y;
			int ix = ev.Wheel.IntegerX;
			int iy = ev.Wheel.IntegerY;
			if (ev.Wheel.Direction == SDLMouseWheelDirection.Flipped) {
				x = -x;
				y = -y;
				ix = -ix;
				iy = -iy;
			}
			result = HostEvent.ForPointerWheel(
				tick,
				windowOrInvalid(ev.Wheel.WindowID),
				new HostPointerWheelEvent(x, y, ix, iy, ev.Wheel.MouseX, ev.Wheel.MouseY)
			);
			return true;
		}

		case SDLEventType.GamepadAdded: {
			int instance = ev.Gdevice.Which;
			if (gamepads.ContainsKey(instance))
				break;
			SDLGamepad* handle = SDL.OpenGamepad(instance);
			if (handle is null)
				break; // e.g. unplugged again in the meantime
			var id = GamepadId.Allocate();
			gamepads.Add(instance, (id, (nint)handle));
			result = HostEvent.GamepadAdded(tick, id);
			return true;
		}
		case SDLEventType.GamepadRemoved: {
			if (!gamepads.Remove(ev.Gdevice.Which, out (GamepadId Id, nint Handle) pad))
				break;
			SDL.CloseGamepad((SDLGamepad*)pad.Handle);
			result = HostEvent.GamepadRemoved(tick, pad.Id);
			return true;
		}
		case SDLEventType.GamepadAxisMotion: {
			if (!gamepads.TryGetValue(ev.Gaxis.Which, out (GamepadId Id, nint Handle) pad))
				break;
			GamepadAxis axis = TranslateGamepadAxis((SDLGamepadAxis)ev.Gaxis.Axis);
			if (axis == GamepadAxis.Unknown)
				break;
			result = HostEvent.ForGamepadAxis(tick, new HostGamepadAxisEvent(pad.Id, axis, NormalizeGamepadAxis(axis, ev.Gaxis.Value)));
			return true;
		}
		case SDLEventType.GamepadButtonDown:
		case SDLEventType.GamepadButtonUp: {
			if (!gamepads.TryGetValue(ev.Gbutton.Which, out (GamepadId Id, nint Handle) pad))
				break;
			GamepadButton button = TranslateGamepadButton((SDLGamepadButton)ev.Gbutton.Button);
			result = HostEvent.ForGamepadButton(tick, new HostGamepadButtonEvent(pad.Id, button, edge(ev.Gbutton.Down)));
			return true;
		}
		}
		result = default;
		return false;
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the context.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the context has been disposed.
	/// </exception>
	public bool WaitUntil(HostTick deadline) {
		context.CheckAccess();
		for (;;) {
			HostDuration remaining = deadline - context.Clock.Now;
			if (remaining < WaitGranularity)
				return false;
			int ms = (int)Math.Min(remaining.Ns / 1_000_000, int.MaxValue);
			if (SDL.WaitEventTimeout(null, ms))
				return true;
		}
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the context.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the context has been disposed.
	/// </exception>
	public void WaitIndefinitely() {
		context.CheckAccess();
		SdlException.Check(SDL.WaitEvent(null));
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Does nothing if the context has been disposed.
	/// </remarks>
	public void Wake() {
		if (Volatile.Read(ref shutDown) != 0 || Interlocked.Exchange(ref wakePending, 1) != 0)
			return;
		SDLEvent ev = default;
		ev.Type = wakeEventType;
		if (!SDL.PushEvent(&ev))
			Volatile.Write(ref wakePending, 0); // let a later call retry
	}

	internal void Shutdown() {
		Volatile.Write(ref shutDown, 1);
		foreach ((GamepadId _, nint handle) in gamepads.Values)
			SDL.CloseGamepad((SDLGamepad*)handle);
		gamepads.Clear();
	}

	private bool window(HostEventKind kind, in SDLEvent ev, HostTick tick, out HostEvent result, out SdlWindow? owner) {
		if (!context.TryGetWindow(ev.Window.WindowID, out owner)) {
			result = default;
			return false;
		}
		result = HostEvent.ForWindow(kind, tick, owner.Id);
		return true;
	}

	private HostWindowId windowOrInvalid(uint sdlWindowId) =>
		context.TryGetWindow(sdlWindowId, out SdlWindow? w) ? w.Id : default;

	private static EdgeType edge(byte down) => down != 0 ? EdgeType.Press : EdgeType.Release;
}
