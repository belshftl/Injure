// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using SDL3;
using Injure.Host;
using Injure.Input;
using static Injure.Sdl.SdlInputTranslation;

namespace Injure.Sdl;

/// <summary>
/// <see cref="IHostEventSource"/> for SDL's event queue.
/// </summary>
/// <remarks>
/// <para>
/// Obtained from <see cref="SdlInstance.Events"/>. Everything except <see cref="Wake()"/> and
/// <see cref="Clock"/> is bound to the instance's thread, like the rest of <c>Injure.Sdl</c>.
/// </para>
/// <para>
/// SDL events that have no <see cref="HostEvent"/> equivalent, and window events for windows that
/// weren't created through <see cref="SdlWindow"/>, are skipped by
/// <see cref="TryPoll(out HostEvent)"/>. To see them, poll with
/// <see cref="DangerousGetNextRaw()"/> and translate the rest with
/// <see cref="DangerousCreateHostEvent(in SDL.Event, out HostEvent)"/>.
/// </para>
/// <para>
/// Gamepads are opened when SDL reports them and closed when they are removed, so gamepad events
/// arrive as long as <see cref="SdlInitOptions.Gamepad"/> was set.
/// </para>
/// </remarks>
public sealed partial class SdlEventSource : IHostEventSource {
	// SDL3-CS only binds the overloads that take an event, which removes it from the queue
	private static partial class Native {
		[LibraryImport("SDL3")]
		[return: MarshalAs(UnmanagedType.I1)]
		public static partial bool SDL_WaitEvent(nint ev);

		[LibraryImport("SDL3")]
		[return: MarshalAs(UnmanagedType.I1)]
		public static partial bool SDL_WaitEventTimeout(nint ev, int timeoutMs);
	}

	private readonly SdlInstance sdl;
	private readonly uint wakeEventType;
	private readonly Dictionary<uint, (GamepadId Id, nint Handle)> gamepads = new(); // by SDL_JoystickID
	private int wakePending = 0;
	private int shutDown = 0;

	internal SdlEventSource(SdlInstance sdl) {
		this.sdl = sdl;
		wakeEventType = SDL.RegisterEvents(1);
		if (wakeEventType == 0)
			throw SdlException.FromLastError("SDL_RegisterEvents");
	}

	/// <inheritdoc/>
	public IHostClock Clock => sdl.Clock;

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// <see cref="WaitUntil(HostTick)"/> may return up to this much before its deadline; a caller that
	/// needs better precision has to wait out the remainder itself.
	/// </para>
	/// <para>
	/// One millisecond, the resolution of <c>SDL_WaitEventTimeout</c>.
	/// </para>
	/// </remarks>
	public HostDuration WaitGranularity { get; } = HostDuration.FromMs(1);

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the instance.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the instance has been disposed.
	/// </exception>
	public bool TryPoll(out HostEvent ev) {
		while (DangerousGetNextRaw() is SDL.Event raw)
			if (DangerousCreateHostEvent(in raw, out ev))
				return true;
		ev = default;
		return false;
	}

	/// <summary>
	/// Removes and returns the next pending raw SDL event, if there is one. Never blocks.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the instance.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the instance has been disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Returns <see langword="null"/> if no event is pending. Events used internally for
	/// <see cref="Wake()"/> are filtered out. Every returned event <b>must</b> be passed to
	/// <see cref="DangerousCreateHostEvent(in SDL.Event, out HostEvent)"/> exactly once, even ones the
	/// caller handles itself, since translation also keeps track of gamepads.
	/// </para>
	/// <para>
	/// <b>The return type is not a stable API and may change without notice.</b> See
	/// <c>docs/conventions/dangerous-get-create.md</c>.
	/// </para>
	/// </remarks>
	public SDL.Event? DangerousGetNextRaw() {
		sdl.CheckAccess();
		while (SDL.PollEvent(out SDL.Event e)) {
			if (e.Type == wakeEventType) {
				Volatile.Write(ref wakePending, 0);
				continue;
			}
			return e;
		}
		return null;
	}

	/// <summary>
	/// Translates a raw SDL event obtained from <see cref="DangerousGetNextRaw()"/>.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> if the event has no <see cref="HostEvent"/> equivalent.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the instance.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the instance has been disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Stateful: gamepad IDs are assigned and gamepads opened/closed here, so each event must be
	/// translated exactly once.
	/// </para>
	/// <para>
	/// <b>The parameter type of <paramref name="ev"/> is not a stable API and may change without
	/// notice.</b> See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </para>
	/// </remarks>
	public bool DangerousCreateHostEvent(in SDL.Event ev, out HostEvent result) {
		if (!translate(in ev, out result, out SdlWindow? owner))
			return false;
		owner?.ApplyEvent(in result);
		return true;
	}

	// owner is set for Window* events, whose window state needs updating
	private bool translate(in SDL.Event ev, out HostEvent result, out SdlWindow? owner) {
		sdl.CheckAccess();
		owner = null;
		HostTick tick = sdl.Clock.FromSdlTimestamp(ev.Common.Timestamp);
		switch ((SDL.EventType)ev.Type) {
		case SDL.EventType.Quit:
			result = HostEvent.Quit(tick);
			return true;

		case SDL.EventType.WindowCloseRequested: return window(HostEventKind.WindowCloseRequested, in ev, tick, out result, out owner);
		case SDL.EventType.WindowShown: return window(HostEventKind.WindowShown, in ev, tick, out result, out owner);
		case SDL.EventType.WindowHidden: return window(HostEventKind.WindowHidden, in ev, tick, out result, out owner);
		case SDL.EventType.WindowExposed: return window(HostEventKind.WindowExposed, in ev, tick, out result, out owner);
		case SDL.EventType.WindowMinimized: return window(HostEventKind.WindowMinimized, in ev, tick, out result, out owner);
		case SDL.EventType.WindowMaximized: return window(HostEventKind.WindowMaximized, in ev, tick, out result, out owner);
		case SDL.EventType.WindowRestored: return window(HostEventKind.WindowRestored, in ev, tick, out result, out owner);
		case SDL.EventType.WindowEnterFullscreen: return window(HostEventKind.WindowEnteredFullscreen, in ev, tick, out result, out owner);
		case SDL.EventType.WindowLeaveFullscreen: return window(HostEventKind.WindowLeftFullscreen, in ev, tick, out result, out owner);
		case SDL.EventType.WindowFocusGained: return window(HostEventKind.WindowFocusGained, in ev, tick, out result, out owner);
		case SDL.EventType.WindowFocusLost: return window(HostEventKind.WindowFocusLost, in ev, tick, out result, out owner);
		case SDL.EventType.WindowMouseEnter: return window(HostEventKind.WindowPointerEntered, in ev, tick, out result, out owner);
		case SDL.EventType.WindowMouseLeave: return window(HostEventKind.WindowPointerLeft, in ev, tick, out result, out owner);
		case SDL.EventType.WindowMoved:
			if (!sdl.TryGetWindow(ev.Window.WindowID, out SdlWindow? moved))
				break;
			owner = moved;
			result = HostEvent.WindowMoved(tick, moved.Id, ev.Window.Data1, ev.Window.Data2);
			return true;
		case SDL.EventType.WindowResized:
			if (!sdl.TryGetWindow(ev.Window.WindowID, out SdlWindow? resized))
				break;
			owner = resized;
			result = HostEvent.WindowResized(tick, resized.Id, ev.Window.Data1, ev.Window.Data2);
			return true;
		case SDL.EventType.WindowPixelSizeChanged:
			if (!sdl.TryGetWindow(ev.Window.WindowID, out SdlWindow? pxResized))
				break;
			owner = pxResized;
			result = HostEvent.WindowPixelSizeChanged(tick, pxResized.Id, ev.Window.Data1, ev.Window.Data2);
			return true;
		case SDL.EventType.WindowDisplayScaleChanged:
			if (!sdl.TryGetWindow(ev.Window.WindowID, out SdlWindow? rescaled))
				break;
			owner = rescaled;
			result = HostEvent.WindowDisplayScaleChanged(tick, rescaled.Id, SDL.GetWindowDisplayScale(rescaled.DangerousGetHandle()));
			return true;

		case SDL.EventType.KeyDown:
		case SDL.EventType.KeyUp:
			result = HostEvent.ForKey(
				tick,
				windowOrInvalid(ev.Key.WindowID),
				new HostKeyEvent(TranslateScancode(ev.Key.Scancode), edge(ev.Key.Down), ev.Key.Repeat)
			);
			return true;
		case SDL.EventType.TextInput:
			string? text = Marshal.PtrToStringUTF8(ev.Text.Text);
			if (text is null)
				break;
			result = HostEvent.ForText(tick, windowOrInvalid(ev.Text.WindowID), text);
			return true;
		case SDL.EventType.MouseMotion:
			result = HostEvent.ForPointerMove(
				tick,
				windowOrInvalid(ev.Motion.WindowID),
				new HostPointerMoveEvent(ev.Motion.X, ev.Motion.Y, ev.Motion.XRel, ev.Motion.YRel)
			);
			return true;
		case SDL.EventType.MouseButtonDown:
		case SDL.EventType.MouseButtonUp:
			result = HostEvent.ForPointerButton(
				tick,
				windowOrInvalid(ev.Button.WindowID),
				new HostPointerButtonEvent(TranslatePointerButton(ev.Button.Button), edge(ev.Button.Down), ev.Button.Clicks, ev.Button.X, ev.Button.Y)
			);
			return true;
		case SDL.EventType.MouseWheel: {
			float x = ev.Wheel.X;
			float y = ev.Wheel.Y;
			int ix = ev.Wheel.IntegerX;
			int iy = ev.Wheel.IntegerY;
			if (ev.Wheel.Direction == SDL.MouseWheelDirection.Flipped) {
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

		case SDL.EventType.GamepadAdded: {
			uint instance = ev.GDevice.Which;
			if (gamepads.ContainsKey(instance))
				break;
			nint handle = SDL.OpenGamepad(instance); // SDL_Gamepad*
			if (handle == 0)
				break; // e.g. unplugged again in the meantime
			var id = GamepadId.Allocate();
			gamepads.Add(instance, (id, handle));
			result = HostEvent.GamepadAdded(tick, id);
			return true;
		}
		case SDL.EventType.GamepadRemoved: {
			if (!gamepads.Remove(ev.GDevice.Which, out (GamepadId Id, nint Handle) pad))
				break;
			SDL.CloseGamepad(pad.Handle);
			result = HostEvent.GamepadRemoved(tick, pad.Id);
			return true;
		}
		case SDL.EventType.GamepadAxisMotion: {
			if (!gamepads.TryGetValue(ev.GAxis.Which, out (GamepadId Id, nint Handle) pad))
				break;
			GamepadAxis axis = TranslateGamepadAxis((SDL.GamepadAxis)ev.GAxis.Axis);
			if (axis == GamepadAxis.Unknown)
				break;
			result = HostEvent.ForGamepadAxis(tick, new HostGamepadAxisEvent(pad.Id, axis, NormalizeGamepadAxis(axis, ev.GAxis.Value)));
			return true;
		}
		case SDL.EventType.GamepadButtonDown:
		case SDL.EventType.GamepadButtonUp: {
			if (!gamepads.TryGetValue(ev.GButton.Which, out (GamepadId Id, nint Handle) pad))
				break;
			GamepadButton button = TranslateGamepadButton((SDL.GamepadButton)ev.GButton.Button);
			result = HostEvent.ForGamepadButton(tick, new HostGamepadButtonEvent(pad.Id, button, edge(ev.GButton.Down)));
			return true;
		}
		}
		result = default;
		return false;
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the instance.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the instance has been disposed.
	/// </exception>
	public bool WaitUntil(HostTick deadline) {
		sdl.CheckAccess();
		for (;;) {
			HostDuration remaining = deadline - sdl.Clock.Now;
			if (remaining < WaitGranularity)
				return false;
			int ms = (int)Math.Min(remaining.Ns / 1_000_000, int.MaxValue);
			if (Native.SDL_WaitEventTimeout(0, ms))
				return true;
		}
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created the instance.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the instance has been disposed.
	/// </exception>
	public void WaitIndefinitely() {
		sdl.CheckAccess();
		SdlException.Check(Native.SDL_WaitEvent(0));
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// Calls made before the waiting thread gets to run again may coalesce into one wakeup.
	/// </para>
	/// <para>
	/// Does nothing if the instance has been disposed.
	/// </para>
	/// </remarks>
	public void Wake() {
		if (Volatile.Read(ref shutDown) != 0 || Interlocked.Exchange(ref wakePending, 1) != 0)
			return;
		SDL.Event ev = default;
		ev.Type = wakeEventType;
		if (!SDL.PushEvent(ref ev))
			Volatile.Write(ref wakePending, 0); // let a later call retry
	}

	internal void Shutdown() {
		Volatile.Write(ref shutDown, 1);
		foreach ((GamepadId _, nint handle) in gamepads.Values)
			SDL.CloseGamepad(handle);
		gamepads.Clear();
	}

	private bool window(HostEventKind kind, in SDL.Event ev, HostTick tick, out HostEvent result, out SdlWindow? owner) {
		if (!sdl.TryGetWindow(ev.Window.WindowID, out owner)) {
			result = default;
			return false;
		}
		result = HostEvent.ForWindow(kind, tick, owner.Id);
		return true;
	}

	private HostWindowId windowOrInvalid(uint sdlWindowId) =>
		sdl.TryGetWindow(sdlWindowId, out SdlWindow? w) ? w.Id : default;

	private static EdgeType edge(bool down) => down ? EdgeType.Press : EdgeType.Release;
}
