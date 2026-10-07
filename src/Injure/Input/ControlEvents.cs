// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.DevAnalyzers.Attributes;
using Injure.Host;

namespace Injure.Input;

[ClosedEnum]
public readonly partial struct ButtonActionEventInfoKind {
	public enum Case {
		None,
		Pointer,
	}
}

public readonly record struct PointerButtonActionInfo(
	HostWindowId Window, // window X/Y are relative to; invalid if unknown
	float X,
	float Y,
	int Clicks
);

public readonly struct ButtonActionEventInfo {
	private readonly PointerButtonActionInfo pointer;

	public ButtonActionEventInfoKind Kind { get; }
	public PointerButtonActionInfo Pointer =>
		Kind == ButtonActionEventInfoKind.Pointer ? pointer : throw new InvalidOperationException("this button action doesn't come from a pointer button");

	private ButtonActionEventInfo(ButtonActionEventInfoKind kind, PointerButtonActionInfo pointer) {
		Kind = kind;
		this.pointer = pointer;
	}

	public static readonly ButtonActionEventInfo None = default;
	public static ButtonActionEventInfo FromPointer(HostWindowId window, float x, float y, int clicks) =>
		new(ButtonActionEventInfoKind.Pointer, new PointerButtonActionInfo(window, x, y, clicks));
	public bool TryGetPointer(out PointerButtonActionInfo info) {
		if (Kind == ButtonActionEventInfoKind.Pointer) {
			info = pointer;
			return true;
		}
		info = default;
		return false;
	}
}

[ClosedEnum]
public readonly partial struct ImpulseAxisActionEventInfoKind {
	public enum Case {
		None,
		Pointer,
	}
}

public readonly record struct PointerImpulseAxisActionInfo(
	HostWindowId Window, // window X/Y are relative to; invalid if unknown
	float X,
	float Y,
	int IntegerAmount
);

public readonly struct ImpulseAxisActionEventInfo {
	private readonly PointerImpulseAxisActionInfo pointer;

	public ImpulseAxisActionEventInfoKind Kind { get; }
	public PointerImpulseAxisActionInfo Pointer => Kind == ImpulseAxisActionEventInfoKind.Pointer
		? pointer
		: throw new InvalidOperationException("this impulse axis action doesn't come from pointer scroll");

	private ImpulseAxisActionEventInfo(ImpulseAxisActionEventInfoKind kind, PointerImpulseAxisActionInfo pointer) {
		Kind = kind;
		this.pointer = pointer;
	}

	public static readonly ImpulseAxisActionEventInfo None = default;
	public static ImpulseAxisActionEventInfo FromPointer(HostWindowId window, float x, float y, int clicks) =>
		new(ImpulseAxisActionEventInfoKind.Pointer, new PointerImpulseAxisActionInfo(window, x, y, clicks));
	public bool TryGetPointer(out PointerImpulseAxisActionInfo info) {
		if (Kind == ImpulseAxisActionEventInfoKind.Pointer) {
			info = pointer;
			return true;
		}
		info = default;
		return false;
	}
}

public abstract record ControlEvent(HostTick Tick);

public sealed record ButtonActionEvent(
	HostTick Tick,
	ActionId Action,
	EdgeType Edge,
	ButtonActionEventInfo Info = default
) : ControlEvent(Tick);

public sealed record StateAxisActionEvent(
	HostTick Tick,
	ActionId Action,
	float Value
) : ControlEvent(Tick);

public sealed record StateAxis2DActionEvent(
	HostTick Tick,
	ActionId Action,
	Vector2 Value
) : ControlEvent(Tick);

public sealed record ImpulseAxisActionEvent(
	HostTick Tick,
	ActionId Action,
	float Amount,
	ImpulseAxisActionEventInfo Info = default
) : ControlEvent(Tick);

public sealed record PointerMoveControlEvent(
	HostTick Tick,
	HostWindowId Window,
	float X,
	float Y,
	Vector2 Delta
) : ControlEvent(Tick);

public sealed record TextEnteredControlEvent(
	HostTick Tick,
	HostWindowId Window,
	string Text
) : ControlEvent(Tick);
