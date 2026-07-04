// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Input;
using Injure.Primitives;
using Injure.Runtime;
using Injure.Time;

namespace Injure.Ui;

public sealed class UiRoot(UiCanvasPolicy canvasPolicy) {
	public UiCanvasPolicy CanvasPolicy { get; set; } = canvasPolicy;
	public UiWidget? RootWidget { get; set; }

	public UiWidget? HoveredWidget { get; private set; }
	public UiWidget? FocusedWidget { get; private set; }
	public UiWidget? CapturedPointerWidget { get; private set; }
	public UiCanvasTransform CanvasTransform { get; private set; }

	public void Focus(UiWidget? widget) {
		if (ReferenceEquals(FocusedWidget, widget))
			return;
		UiWidget? old = FocusedWidget;
		FocusedWidget = widget;
		if (old is IUiFocusSink oldSink) {
			var ctx = UiEventContext.Create(this, old);
			oldSink.OnFocus(ref ctx, new UiFocusEvent(false));
		}
		if (widget is IUiFocusSink newSink) {
			var ctx = UiEventContext.Create(this, widget);
			newSink.OnFocus(ref ctx, new UiFocusEvent(true));
		}
	}

	public void CapturePointer(UiWidget widget) {
		ArgumentNullException.ThrowIfNull(widget);
		CapturedPointerWidget = widget;
	}

	public void ReleasePointerCapture(UiWidget widget) {
		if (ReferenceEquals(CapturedPointerWidget, widget))
			CapturedPointerWidget = null;
	}

	public void Update(in ControlView input, in WindowState window) {
		SizeI drawable = new(window.DrawableWidth, window.DrawableHeight);
		CanvasTransform = UiCanvasLayout.Compute(CanvasPolicy, drawable);

		if (RootWidget is null)
			return;
		UiLayoutContext ctx = new(this, CanvasTransform);
		RootWidget.Measure(in ctx, new UiSizeConstraint(CanvasTransform.LogicalRect.Size));
		RootWidget.Arrange(CanvasTransform.LogicalRect);

		processControlEvents(input);
	}

	public void Render(Canvas cv) {
		if (RootWidget is null || !RootWidget.Visible)
			return;

		using (cv.PushParams(
			Scissor: CanvasScissor.Set(CanvasTransform.ViewportRect),
			Transform: Matrix3x2.CreateScale(CanvasTransform.Scale) * Matrix3x2.CreateTranslation(CanvasTransform.ViewportRect.Position.ToVector2())
		)) {
			UiRenderContext ctx = new(this, CanvasTransform);
			RootWidget.Render(cv, in ctx);
		}
	}

	private void processControlEvents(in ControlView input) {
		for (int i = 0; i < input.Events.Length; i++)
			switch (input.Events[i]) {
			case PointerMoveControlEvent move:
				handlePointerMove(move);
				break;
			case ButtonActionEvent btn when btn.Info.Kind == ButtonActionEventInfoKind.Pointer:
				handlePointerButton(btn);
				break;
			case ImpulseAxisActionEvent axis when axis.Info.Kind == ImpulseAxisActionEventInfoKind.Pointer:
				handleScroll(axis);
				break;
			case TextEnteredControlEvent text:
				handleText(text);
				break;
			}
	}

	private void handlePointerMove(PointerMoveControlEvent ev) {
		Vector2 logicalPos = UiCanvasLayout.ScreenToLogical(CanvasTransform, new Vector2(ev.X, ev.Y));
		Vector2 logicalDelta = ev.Delta / CanvasTransform.Scale;
		UiWidget? target = CapturedPointerWidget ?? HitTest(logicalPos);
		updateHover(logicalPos);
		if (target is IUiPointerMoveSink sink) {
			var ctx = UiEventContext.Create(this, target);
			sink.OnPointerMove(
				ref ctx,
				new UiPointerMoveEvent(
					Tick: ev.Tick,
					Position: logicalPos,
					Delta: logicalDelta
				)
			);
		}
	}

	private void handlePointerButton(ButtonActionEvent ev) {
		PointerButtonActionInfo p = ev.Info.Pointer;
		Vector2 logicalPos = UiCanvasLayout.ScreenToLogical(CanvasTransform, new Vector2(p.X, p.Y));
		UiWidget? target = CapturedPointerWidget ?? HitTest(logicalPos);
		if (target is null)
			return;
		if (target.Focusable)
			Focus(target);
		if (target is IUiPointerButtonSink sink) {
			var ctx = UiEventContext.Create(this, target);
			sink.OnPointerButton(
				ref ctx,
				new UiPointerButtonEventj(
					Tick: ev.Tick,
					Position: logicalPos,
					Button: PointerButton.Left, // TODO
					Edge: ev.Edge,
					Clicks: p.Clicks
				)
			);
		}
	}

	private void handleScroll(ImpulseAxisActionEvent ev) {
		PointerImpulseAxisActionInfo p = ev.Info.Pointer;
		Vector2 logicalPos = UiCanvasLayout.ScreenToLogical(CanvasTransform, new Vector2(p.X, p.Y));

		UiWidget? target = HoveredWidget ?? FocusedWidget;
		if (target is not IUiScrollSink sink)
			return;

		Vector2 amount = new(0f, ev.Amount); // TODO: assumes this is a scroll-y action
		var ctx = UiEventContext.Create(this, target);
		sink.OnScroll(
			ref ctx,
			new UiScrollEvent(
				Tick: ev.Tick,
				Position: logicalPos,
				Amount: amount,
				IntegerAmount: new Vector2Int(0, p.IntegerAmount)
			)
		);
	}

	private void handleText(TextEnteredControlEvent ev) {
		if (FocusedWidget is not IUiTextInputSink sink)
			return;

		var ctx = UiEventContext.Create(this, FocusedWidget);
		sink.OnTextInput(
			ref ctx,
			new UiTextInputEvent(
				Tick: ev.Tick,
				Text: ev.Text
			)
		);
	}

	private void updateHover(Vector2 pos) {
		UiWidget? hit = HitTest(pos);
		if (ReferenceEquals(hit, HoveredWidget))
			return;

		UiWidget? old = HoveredWidget;
		HoveredWidget = hit;

		if (old is IUiHoverSink oldSink) {
			var ctx = UiEventContext.Create(this, old);
			oldSink.OnHover(
				ref ctx,
				new UiHoverEvent(
					Tick: MonoTick.Zero,
					Position: pos,
					Hovered: false
				)
			);
		}
		if (hit is IUiHoverSink newSink) {
			var ctx = UiEventContext.Create(this, hit);
			newSink.OnHover(
				ref ctx,
				new UiHoverEvent(
					Tick: MonoTick.Zero,
					Position: pos,
					Hovered: true
				)
			);
		}
	}

	public UiWidget? HitTest(Vector2 pos) {
		if (RootWidget is null || !RootWidget.Visible)
			return null;
		return hitTestRecursive(RootWidget, pos);
	}

	private static UiWidget? hitTestRecursive(UiWidget widget, Vector2 pos) {
		if (!widget.HitTest(pos))
			return null;

		IReadOnlyList<UiWidget> children = widget.Children;
		for (int i = children.Count - 1; i >= 0; i--) {
			UiWidget child = children[i];
			UiWidget? hit = hitTestRecursive(child, pos);
			if (hit is not null)
				return hit;
		}

		return widget;
	}
}
