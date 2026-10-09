// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using Injure.DevAnalyzers.Attributes;
using Injure.Input;

namespace Injure.Host;

/// <summary>
/// The kind of a <see cref="HostEvent"/>, which determines its payload.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct HostEventKind {
	public enum Case {
		/// <summary>
		/// The application as a whole was asked to quit, e.g. by the OS or by closing the last
		/// window. No payload; no window.
		/// </summary>
		Quit = 1,

		/// <summary>
		/// The user asked to close a window. Nothing is closed automatically. No payload.
		/// </summary>
		WindowCloseRequested,
		/// <summary>No payload.</summary>
		WindowShown,
		/// <summary>No payload.</summary>
		WindowHidden,
		/// <summary>
		/// The window's contents need to be redrawn. No payload.
		/// </summary>
		WindowExposed,
		/// <summary>Payload: <see cref="HostEvent.Position"/>, in screen coordinates.</summary>
		WindowMoved,
		/// <summary>Payload: <see cref="HostEvent.Size"/>, in screen coordinates.</summary>
		WindowResized,
		/// <summary>Payload: <see cref="HostEvent.Size"/>, in physical pixels.</summary>
		WindowPixelSizeChanged,
		/// <summary>Payload: <see cref="HostEvent.Scale"/>.</summary>
		WindowDisplayScaleChanged,
		/// <summary>No payload.</summary>
		WindowMinimized,
		/// <summary>No payload.</summary>
		WindowMaximized,
		/// <summary>No payload.</summary>
		WindowRestored,
		/// <summary>No payload.</summary>
		WindowEnteredFullscreen,
		/// <summary>No payload.</summary>
		WindowLeftFullscreen,
		/// <summary>No payload.</summary>
		WindowFocusGained,
		/// <summary>No payload.</summary>
		WindowFocusLost,
		/// <summary>No payload.</summary>
		WindowPointerEntered,
		/// <summary>No payload.</summary>
		WindowPointerLeft,

		/// <summary>Payload: <see cref="HostEvent.Key"/>.</summary>
		Key,
		/// <summary>Payload: <see cref="HostEvent.Text"/>.</summary>
		TextInput,
		/// <summary>Payload: <see cref="HostEvent.PointerMove"/>.</summary>
		PointerMove,
		/// <summary>Payload: <see cref="HostEvent.PointerButton"/>.</summary>
		PointerButton,
		/// <summary>Payload: <see cref="HostEvent.PointerWheel"/>.</summary>
		PointerWheel,

		/// <summary>Payload: <see cref="HostEvent.Gamepad"/>; no window.</summary>
		GamepadAdded,
		/// <summary>Payload: <see cref="HostEvent.Gamepad"/>; no window.</summary>
		GamepadRemoved,
		/// <summary>Payload: <see cref="HostEvent.GamepadAxis"/>; no window.</summary>
		GamepadAxis,
		/// <summary>Payload: <see cref="HostEvent.GamepadButton"/>; no window.</summary>
		GamepadButton,
	}
}

/// <summary>
/// Payload of <see cref="HostEventKind.Key"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Edge"/> is.
/// </remarks>
public readonly record struct HostKeyEvent(Key Key, EdgeType Edge, bool Repeat);

/// <summary>
/// Payload of <see cref="HostEventKind.PointerMove"/>, in window coordinates.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is a zero-length move at the origin.
/// </remarks>
public readonly record struct HostPointerMoveEvent(float X, float Y, float DeltaX, float DeltaY);

/// <summary>
/// Payload of <see cref="HostEventKind.PointerButton"/>, in window coordinates.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Edge"/> is.
/// </remarks>
public readonly record struct HostPointerButtonEvent(PointerButton Button, EdgeType Edge, int Clicks, float X, float Y);

/// <summary>
/// Payload of <see cref="HostEventKind.PointerWheel"/>. Positive <see cref="Y"/> scrolls away
/// from the user, positive <see cref="X"/> to the right, regardless of "natural scrolling".
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is a zero scroll at the window origin.
/// </remarks>
public readonly record struct HostPointerWheelEvent(float X, float Y, int IntegerX, int IntegerY, float PointerX, float PointerY);

/// <summary>
/// Payload of <see cref="HostEventKind.GamepadAxis"/>. <see cref="Value"/> is in [-1, 1] for
/// sticks and [0, 1] for triggers.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Gamepad"/> is.
/// </remarks>
public readonly record struct HostGamepadAxisEvent(GamepadId Gamepad, GamepadAxis Axis, float Value);

/// <summary>
/// Payload of <see cref="HostEventKind.GamepadButton"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Gamepad"/> is.
/// </remarks>
public readonly record struct HostGamepadButtonEvent(GamepadId Gamepad, GamepadButton Button, EdgeType Edge);

/// <summary>
/// An event from an <see cref="IHostEventSource"/>: window, input, and application lifecycle
/// events, in a form independent of the backend that produced them.
/// </summary>
/// <remarks>
/// <para>
/// A tagged union: <see cref="Kind"/> determines which payload accessor is valid, and the
/// others throw <see cref="InvalidOperationException"/>. Only <see cref="HostEventKind.TextInput"/>
/// carries a reference (its string); everything else is stored inline.
/// </para>
/// <para>
/// <see cref="Tick"/> is on the clock of the source that produced the event
/// (<see cref="IHostEventSource.Clock"/>).
/// </para>
/// <para>
/// <see cref="Window"/> is valid for all <c>Window*</c> kinds. For keyboard, text, and pointer
/// events, it is the window that had focus, or invalid if the source couldn't attribute the
/// event to a window. It is always invalid for <see cref="HostEventKind.Quit"/> and gamepad events.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct HostEvent {
	/// <remarks>
	/// The <see langword="default"/> value is valid and is <c>(0, 0)</c>.
	/// </remarks>
	[StructLayout(LayoutKind.Sequential)]
	private struct IntPair(int a, int b) {
		public int A = a;
		public int B = b;
	}

	// everything here must be unmanaged and not auto-layout, or overlapping it fails at type load
	/// <remarks>
	/// The <see langword="default"/> value is valid and is the payload of kinds that have none.
	/// </remarks>
	[StructLayout(LayoutKind.Explicit)]
	private struct Payload {
		[FieldOffset(0)] public IntPair Ints;
		[FieldOffset(0)] public float Scale;
		[FieldOffset(0)] public HostKeyEvent Key;
		[FieldOffset(0)] public HostPointerMoveEvent PointerMove;
		[FieldOffset(0)] public HostPointerButtonEvent PointerButton;
		[FieldOffset(0)] public HostPointerWheelEvent PointerWheel;
		[FieldOffset(0)] public GamepadId Gamepad;
		[FieldOffset(0)] public HostGamepadAxisEvent GamepadAxis;
		[FieldOffset(0)] public HostGamepadButtonEvent GamepadButton;
	}

	private readonly Payload payload;
	private readonly string? text;

	/// <summary>
	/// What kind of event this is.
	/// </summary>
	public HostEventKind Kind { get; }

	/// <summary>
	/// When the event happened, on the clock of the source that produced it.
	/// </summary>
	public HostTick Tick { get; }

	/// <summary>
	/// The window this event belongs to; see the type's <c>&lt;remarks&gt;</c> for which kinds
	/// have one.
	/// </summary>
	public HostWindowId Window { get; }

	private HostEvent(HostEventKind kind, HostTick tick, HostWindowId window, Payload payload, string? text = null) {
		Kind = kind;
		Tick = tick;
		Window = window;
		this.payload = payload;
		this.text = text;
	}

	// ==========================================================================
	// payload accessors
	/// <summary>
	/// Payload of <see cref="HostEventKind.WindowMoved"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.WindowMoved"/>.
	/// </exception>
	public (int X, int Y) Position {
		get {
			IntPair p = require(Kind == HostEventKind.WindowMoved).payload.Ints;
			return (p.A, p.B);
		}
	}

	/// <summary>
	/// Payload of <see cref="HostEventKind.WindowResized"/> and
	/// <see cref="HostEventKind.WindowPixelSizeChanged"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is neither of those.
	/// </exception>
	public (int Width, int Height) Size {
		get {
			IntPair p = require(Kind == HostEventKind.WindowResized || Kind == HostEventKind.WindowPixelSizeChanged).payload.Ints;
			return (p.A, p.B);
		}
	}

	/// <summary>
	/// Payload of <see cref="HostEventKind.WindowDisplayScaleChanged"/>: the new display scale.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.WindowDisplayScaleChanged"/>.
	/// </exception>
	public float Scale => require(Kind == HostEventKind.WindowDisplayScaleChanged).payload.Scale;

	/// <summary>
	/// Payload of <see cref="HostEventKind.Key"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.Key"/>.
	/// </exception>
	public HostKeyEvent Key => require(Kind == HostEventKind.Key).payload.Key;

	/// <summary>
	/// Payload of <see cref="HostEventKind.TextInput"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.TextInput"/>.
	/// </exception>
	public string Text => require(Kind == HostEventKind.TextInput).text!;

	/// <summary>
	/// Payload of <see cref="HostEventKind.PointerMove"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.PointerMove"/>.
	/// </exception>
	public HostPointerMoveEvent PointerMove => require(Kind == HostEventKind.PointerMove).payload.PointerMove;

	/// <summary>
	/// Payload of <see cref="HostEventKind.PointerButton"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.PointerButton"/>.
	/// </exception>
	public HostPointerButtonEvent PointerButton => require(Kind == HostEventKind.PointerButton).payload.PointerButton;

	/// <summary>
	/// Payload of <see cref="HostEventKind.PointerWheel"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.PointerWheel"/>.
	/// </exception>
	public HostPointerWheelEvent PointerWheel => require(Kind == HostEventKind.PointerWheel).payload.PointerWheel;

	/// <summary>
	/// Payload of <see cref="HostEventKind.GamepadAdded"/> and
	/// <see cref="HostEventKind.GamepadRemoved"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is neither of those.
	/// </exception>
	public GamepadId Gamepad => require(Kind == HostEventKind.GamepadAdded || Kind == HostEventKind.GamepadRemoved).payload.Gamepad;

	/// <summary>
	/// Payload of <see cref="HostEventKind.GamepadAxis"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.GamepadAxis"/>.
	/// </exception>
	public HostGamepadAxisEvent GamepadAxis => require(Kind == HostEventKind.GamepadAxis).payload.GamepadAxis;

	/// <summary>
	/// Payload of <see cref="HostEventKind.GamepadButton"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="HostEventKind.GamepadButton"/>.
	/// </exception>
	public HostGamepadButtonEvent GamepadButton => require(Kind == HostEventKind.GamepadButton).payload.GamepadButton;

	private HostEvent require(bool ok) {
		if (!ok)
			throw new InvalidOperationException($"this payload is not available on a {Kind} event");
		return this;
	}

	// ==========================================================================
	// construction
	/// <summary>
	/// Creates a <see cref="HostEventKind.Quit"/> event.
	/// </summary>
	public static HostEvent Quit(HostTick tick) => new(HostEventKind.Quit, tick, default, default);

	/// <summary>
	/// Creates a <c>Window*</c> event that has no payload.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="kind"/> is not a payloadless <c>Window*</c> kind, or if
	/// <paramref name="window"/> is invalid.
	/// </exception>
	public static HostEvent ForWindow(HostEventKind kind, HostTick tick, HostWindowId window) {
		return kind.Tag switch {
			HostEventKind.Case.WindowCloseRequested
				or HostEventKind.Case.WindowShown
				or HostEventKind.Case.WindowHidden
				or HostEventKind.Case.WindowExposed
				or HostEventKind.Case.WindowMinimized
				or HostEventKind.Case.WindowMaximized
				or HostEventKind.Case.WindowRestored
				or HostEventKind.Case.WindowEnteredFullscreen
				or HostEventKind.Case.WindowLeftFullscreen
				or HostEventKind.Case.WindowFocusGained
				or HostEventKind.Case.WindowFocusLost
				or HostEventKind.Case.WindowPointerEntered
				or HostEventKind.Case.WindowPointerLeft => new HostEvent(kind, tick, requireWindow(window), default),
			_ => throw new ArgumentException($"{kind} is not a payloadless window event kind", nameof(kind)),
		};
	}

	/// <summary>
	/// Creates a <see cref="HostEventKind.WindowMoved"/> event.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="window"/> is invalid.
	/// </exception>
	public static HostEvent WindowMoved(HostTick tick, HostWindowId window, int x, int y) =>
		new(HostEventKind.WindowMoved, tick, requireWindow(window), new Payload { Ints = new IntPair(x, y) });

	/// <summary>
	/// Creates a <see cref="HostEventKind.WindowResized"/> event.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="window"/> is invalid.
	/// </exception>
	public static HostEvent WindowResized(HostTick tick, HostWindowId window, int width, int height) =>
		new(HostEventKind.WindowResized, tick, requireWindow(window), new Payload { Ints = new IntPair(width, height) });

	/// <summary>
	/// Creates a <see cref="HostEventKind.WindowPixelSizeChanged"/> event.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="window"/> is invalid.
	/// </exception>
	public static HostEvent WindowPixelSizeChanged(HostTick tick, HostWindowId window, int width, int height) =>
		new(HostEventKind.WindowPixelSizeChanged, tick, requireWindow(window), new Payload { Ints = new IntPair(width, height) });

	/// <summary>
	/// Creates a <see cref="HostEventKind.WindowDisplayScaleChanged"/> event.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="window"/> is invalid.
	/// </exception>
	public static HostEvent WindowDisplayScaleChanged(HostTick tick, HostWindowId window, float scale) =>
		new(HostEventKind.WindowDisplayScaleChanged, tick, requireWindow(window), new Payload { Scale = scale });

	/// <summary>
	/// Creates a <see cref="HostEventKind.Key"/> event.
	/// </summary>
	public static HostEvent ForKey(HostTick tick, HostWindowId window, in HostKeyEvent key) =>
		new(HostEventKind.Key, tick, window, new Payload { Key = key });

	/// <summary>
	/// Creates a <see cref="HostEventKind.TextInput"/> event.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="text"/> is <see langword="null"/>.
	/// </exception>
	public static HostEvent ForText(HostTick tick, HostWindowId window, string text) {
		ArgumentNullException.ThrowIfNull(text);
		return new HostEvent(HostEventKind.TextInput, tick, window, default, text);
	}

	/// <summary>
	/// Creates a <see cref="HostEventKind.PointerMove"/> event.
	/// </summary>
	public static HostEvent ForPointerMove(HostTick tick, HostWindowId window, in HostPointerMoveEvent move) =>
		new(HostEventKind.PointerMove, tick, window, new Payload { PointerMove = move });

	/// <summary>
	/// Creates a <see cref="HostEventKind.PointerButton"/> event.
	/// </summary>
	public static HostEvent ForPointerButton(HostTick tick, HostWindowId window, in HostPointerButtonEvent button) =>
		new(HostEventKind.PointerButton, tick, window, new Payload { PointerButton = button });

	/// <summary>
	/// Creates a <see cref="HostEventKind.PointerWheel"/> event.
	/// </summary>
	public static HostEvent ForPointerWheel(HostTick tick, HostWindowId window, in HostPointerWheelEvent wheel) =>
		new(HostEventKind.PointerWheel, tick, window, new Payload { PointerWheel = wheel });

	/// <summary>
	/// Creates a <see cref="HostEventKind.GamepadAdded"/> event.
	/// </summary>
	public static HostEvent GamepadAdded(HostTick tick, GamepadId gamepad) =>
		new(HostEventKind.GamepadAdded, tick, default, new Payload { Gamepad = gamepad });

	/// <summary>
	/// Creates a <see cref="HostEventKind.GamepadRemoved"/> event.
	/// </summary>
	public static HostEvent GamepadRemoved(HostTick tick, GamepadId gamepad) =>
		new(HostEventKind.GamepadRemoved, tick, default, new Payload { Gamepad = gamepad });

	/// <summary>
	/// Creates a <see cref="HostEventKind.GamepadAxis"/> event.
	/// </summary>
	public static HostEvent ForGamepadAxis(HostTick tick, in HostGamepadAxisEvent axis) =>
		new(HostEventKind.GamepadAxis, tick, default, new Payload { GamepadAxis = axis });

	/// <summary>
	/// Creates a <see cref="HostEventKind.GamepadButton"/> event.
	/// </summary>
	public static HostEvent ForGamepadButton(HostTick tick, in HostGamepadButtonEvent button) =>
		new(HostEventKind.GamepadButton, tick, default, new Payload { GamepadButton = button });

	private static HostWindowId requireWindow(HostWindowId window) =>
		window.IsValid ? window : throw new ArgumentException("window events need a valid window ID", nameof(window));

	public override string ToString() => $"{Kind} @ {Tick}" + (Window.IsValid ? $" on {Window}" : "");
}
