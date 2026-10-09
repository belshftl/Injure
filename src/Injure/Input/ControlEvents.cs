// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.DevAnalyzers.Attributes;
using Injure.Host;

namespace Injure.Input;

/// <summary>
/// What kind of extra information a <see cref="ButtonActionEventInfo"/> carries.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct ButtonActionEventInfoKind {
	/// <summary>Raw switch tag for <see cref="ButtonActionEventInfoKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// No extra information, e.g. for gamepad buttons and re-synchronization presses.
		/// </summary>
		None,

		/// <summary>
		/// The event came from a keyboard key; see <see cref="KeyButtonActionInfo"/>.
		/// </summary>
		Key,

		/// <summary>
		/// The event came from a pointer button; see <see cref="PointerButtonActionInfo"/>.
		/// </summary>
		Pointer,
	}
}

/// <summary>
/// Where a key button action event happened.
/// </summary>
/// <param name="Window">The window that had keyboard focus; invalid if unknown.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is an event in no window.
/// </remarks>
public readonly record struct KeyButtonActionInfo(HostWindowId Window);

/// <summary>
/// Where a pointer button action event happened.
/// </summary>
/// <param name="Window">
/// The window <see cref="X"/>/<see cref="Y"/> are relative to; invalid if unknown.
/// </param>
/// <param name="X">Horizontal position in <see cref="Window"/>'s coordinates.</param>
/// <param name="Y">Vertical position in <see cref="Window"/>'s coordinates.</param>
/// <param name="Clicks">Click count of the press (1 = single click, 2 = double click, ...).</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a zero-click event at (0, 0) in no window.
/// </remarks>
public readonly record struct PointerButtonActionInfo(
	HostWindowId Window,
	float X,
	float Y,
	int Clicks
);

/// <summary>
/// Extra information about where a <see cref="ButtonActionEvent"/> came from, if any.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and carries no information, same as
/// <see cref="None"/>.
/// </remarks>
public readonly struct ButtonActionEventInfo {
	private readonly KeyButtonActionInfo key;
	private readonly PointerButtonActionInfo pointer;

	/// <summary>What kind of information this carries.</summary>
	public ButtonActionEventInfoKind Kind { get; }

	/// <summary>
	/// The key information, if <see cref="Kind"/> is <see cref="ButtonActionEventInfoKind.Key"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="ButtonActionEventInfoKind.Key"/>.
	/// </exception>
	public KeyButtonActionInfo Key => Kind == ButtonActionEventInfoKind.Key
		? key
		: throw new InvalidOperationException("this button action doesn't come from a key");

	/// <summary>
	/// The pointer information, if <see cref="Kind"/> is
	/// <see cref="ButtonActionEventInfoKind.Pointer"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="ButtonActionEventInfoKind.Pointer"/>.
	/// </exception>
	public PointerButtonActionInfo Pointer => Kind == ButtonActionEventInfoKind.Pointer
		? pointer
		: throw new InvalidOperationException("this button action doesn't come from a pointer button");

	private ButtonActionEventInfo(ButtonActionEventInfoKind kind, KeyButtonActionInfo key, PointerButtonActionInfo pointer) {
		Kind = kind;
		this.key = key;
		this.pointer = pointer;
	}

	/// <summary>No information.</summary>
	public static readonly ButtonActionEventInfo None = default;

	/// <summary>Creates key information.</summary>
	public static ButtonActionEventInfo FromKey(HostWindowId window) =>
		new(ButtonActionEventInfoKind.Key, new KeyButtonActionInfo(window), default);

	/// <summary>Creates pointer information.</summary>
	public static ButtonActionEventInfo FromPointer(HostWindowId window, float x, float y, int clicks) =>
		new(ButtonActionEventInfoKind.Pointer, default, new PointerButtonActionInfo(window, x, y, clicks));

	/// <summary>
	/// Gets the key information, if <see cref="Kind"/> is <see cref="ButtonActionEventInfoKind.Key"/>.
	/// </summary>
	public bool TryGetKey(out KeyButtonActionInfo info) {
		if (Kind == ButtonActionEventInfoKind.Key) {
			info = key;
			return true;
		}
		info = default;
		return false;
	}

	/// <summary>
	/// Gets the pointer information, if <see cref="Kind"/> is
	/// <see cref="ButtonActionEventInfoKind.Pointer"/>.
	/// </summary>
	public bool TryGetPointer(out PointerButtonActionInfo info) {
		if (Kind == ButtonActionEventInfoKind.Pointer) {
			info = pointer;
			return true;
		}
		info = default;
		return false;
	}
}

/// <summary>
/// What kind of extra information an <see cref="ImpulseAxisActionEventInfo"/> carries.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct ImpulseAxisActionEventInfoKind {
	/// <summary>Raw switch tag for <see cref="ImpulseAxisActionEventInfoKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// No extra information.
		/// </summary>
		None,

		/// <summary>
		/// The event came from the pointer wheel; see <see cref="PointerImpulseAxisActionInfo"/>.
		/// </summary>
		Pointer,
	}
}

/// <summary>
/// Where a pointer wheel impulse axis action event happened.
/// </summary>
/// <param name="Window">
/// The window <see cref="X"/>/<see cref="Y"/> are relative to; invalid if unknown.
/// </param>
/// <param name="X">Horizontal pointer position in <see cref="Window"/>'s coordinates.</param>
/// <param name="Y">Vertical pointer position in <see cref="Window"/>'s coordinates.</param>
/// <param name="IntegerAmount">The scroll amount in whole wheel notches, before scaling.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a zero-notch event at (0, 0) in no window.
/// </remarks>
public readonly record struct PointerImpulseAxisActionInfo(
	HostWindowId Window,
	float X,
	float Y,
	int IntegerAmount
);

/// <summary>
/// Extra information about where an <see cref="ImpulseAxisActionEvent"/> came from, if any.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and carries no information, same as
/// <see cref="None"/>.
/// </remarks>
public readonly struct ImpulseAxisActionEventInfo {
	private readonly PointerImpulseAxisActionInfo pointer;

	/// <summary>What kind of information this carries.</summary>
	public ImpulseAxisActionEventInfoKind Kind { get; }

	/// <summary>
	/// The pointer information, if <see cref="Kind"/> is
	/// <see cref="ImpulseAxisActionEventInfoKind.Pointer"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Kind"/> is not <see cref="ImpulseAxisActionEventInfoKind.Pointer"/>.
	/// </exception>
	public PointerImpulseAxisActionInfo Pointer => Kind == ImpulseAxisActionEventInfoKind.Pointer
		? pointer
		: throw new InvalidOperationException("this impulse axis action doesn't come from pointer scroll");

	private ImpulseAxisActionEventInfo(ImpulseAxisActionEventInfoKind kind, PointerImpulseAxisActionInfo pointer) {
		Kind = kind;
		this.pointer = pointer;
	}

	/// <summary>No information.</summary>
	public static readonly ImpulseAxisActionEventInfo None = default;

	/// <summary>Creates pointer information.</summary>
	public static ImpulseAxisActionEventInfo FromPointer(HostWindowId window, float x, float y, int integerAmount) =>
		new(ImpulseAxisActionEventInfoKind.Pointer, new PointerImpulseAxisActionInfo(window, x, y, integerAmount));

	/// <summary>
	/// Gets the pointer information, if <see cref="Kind"/> is
	/// <see cref="ImpulseAxisActionEventInfoKind.Pointer"/>.
	/// </summary>
	public bool TryGetPointer(out PointerImpulseAxisActionInfo info) {
		if (Kind == ImpulseAxisActionEventInfoKind.Pointer) {
			info = pointer;
			return true;
		}
		info = default;
		return false;
	}
}

/// <summary>
/// An event produced by an <see cref="ActionTracker"/>: an action changed, or pointer movement or text
/// that is passed through for every consumer.
/// </summary>
/// <param name="Tick">When the underlying input happened.</param>
public abstract record ControlEvent(HostTick Tick);

/// <summary>
/// A button action was pressed or released.
/// </summary>
/// <remarks>
/// With several inputs bound to the action, every newly pressed input produces a press, but the
/// release only comes once all of them are up.
/// </remarks>
public sealed record ButtonActionEvent(
	HostTick Tick,
	ActionId Action,
	EdgeType Edge,
	ButtonActionEventInfo Info = default
) : ControlEvent(Tick);

/// <summary>
/// A 1D state axis action changed to <see cref="Value"/>.
/// </summary>
public sealed record StateAxisActionEvent(
	HostTick Tick,
	ActionId Action,
	float Value
) : ControlEvent(Tick);

/// <summary>
/// A 2D state axis action changed to <see cref="Value"/>.
/// </summary>
public sealed record StateAxis2dActionEvent(
	HostTick Tick,
	ActionId Action,
	Vector2 Value
) : ControlEvent(Tick);

/// <summary>
/// An impulse axis action received <see cref="Amount"/> (already scaled).
/// </summary>
public sealed record ImpulseAxisActionEvent(
	HostTick Tick,
	ActionId Action,
	float Amount,
	ImpulseAxisActionEventInfo Info = default
) : ControlEvent(Tick);

/// <summary>
/// The pointer moved to (<see cref="X"/>, <see cref="Y"/>) in <see cref="Window"/>'s coordinates.
/// Emitted regardless of bindings.
/// </summary>
public sealed record PointerMoveControlEvent(
	HostTick Tick,
	HostWindowId Window,
	float X,
	float Y,
	Vector2 Delta
) : ControlEvent(Tick);

/// <summary>
/// Text was entered into <see cref="Window"/>. Emitted regardless of bindings.
/// </summary>
/// <remarks>
/// A stopgap until there is a proper text input API; not suitable for text editing.
/// </remarks>
public sealed record TextEnteredControlEvent(
	HostTick Tick,
	HostWindowId Window,
	string Text
) : ControlEvent(Tick);
