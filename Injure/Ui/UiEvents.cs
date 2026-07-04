// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Input;
using Injure.Primitives;
using Injure.Time;

namespace Injure.Ui;

public readonly record struct UiHoverEvent(
	MonoTick Tick,
	Vector2 Position,
	bool Hovered
);

public readonly record struct UiPointerMoveEvent(
	MonoTick Tick,
	Vector2 Position,
	Vector2 Delta
);

public readonly record struct UiPointerButtonEventj(
	MonoTick Tick,
	Vector2 Position,
	PointerButton Button,
	EdgeType Edge,
	int Clicks
);

public readonly record struct UiScrollEvent(
	MonoTick Tick,
	Vector2 Position,
	Vector2 Amount,
	Vector2Int IntegerAmount
);

public readonly record struct UiFocusEvent(
	bool Focused
);

public readonly record struct UiTextInputEvent(
	MonoTick Tick,
	string Text
);

public ref struct UiEventContext {
	public UiRoot Root { get; private set; }
	public UiWidget Target { get; private set; }

	public bool Handled { get; set; }

	private UiEventContext(UiRoot root, UiWidget target) {
		Root = root;
		Target = target;
		Handled = false;
	}

	public static UiEventContext Create(UiRoot root, UiWidget target) => new(root, target);
	public readonly void Focus(UiWidget? widget) => Root.Focus(widget);
	public readonly void CapturePointer() => Root.CapturePointer(Target);
	public readonly void ReleasePointerCapture() => Root.ReleasePointerCapture(Target);
}

public interface IUiHoverSink {
	void OnHover(ref UiEventContext ctx, in UiHoverEvent ev);
}

public interface IUiPointerMoveSink {
	void OnPointerMove(ref UiEventContext ctx, in UiPointerMoveEvent ev);
}

public interface IUiPointerButtonSink {
	void OnPointerButton(ref UiEventContext ctx, in UiPointerButtonEventj ev);
}

public interface IUiScrollSink {
	void OnScroll(ref UiEventContext ctx, in UiScrollEvent ev);
}

public interface IUiFocusSink {
	void OnFocus(ref UiEventContext ctx, in UiFocusEvent ev);
}

public interface IUiTextInputSink {
	void OnTextInput(ref UiEventContext ctx, in UiTextInputEvent ev);
}
