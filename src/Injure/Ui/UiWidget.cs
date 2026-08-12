// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Primitives;

namespace Injure.Ui;

public abstract class UiWidget {
	public UiWidget? Parent { get; private set; }
	public RectF LayoutRect { get; private set; }
	public SizeF DesiredSize { get; private set; }

	public bool Visible { get; set; } = true;
	public bool Enabled { get; set; } = true;
	public bool HitTestVisible { get; set; } = true;
	public bool Focusable { get; set; } = false;

	public UiThickness Margin { get; set; }
	public UiThickness Padding { get; set; }

	public virtual IReadOnlyList<UiWidget> Children => Array.Empty<UiWidget>();

	internal void AttachToParent(UiWidget? p) {
		Parent = p;
	}

	public SizeF Measure(in UiLayoutContext ctx, in UiSizeConstraint constraint) {
		DesiredSize = Visible ? MeasureCore(in ctx, in constraint) : SizeF.Zero;
		return DesiredSize;
	}
	protected abstract SizeF MeasureCore(in UiLayoutContext ctx, in UiSizeConstraint constraint);

	public void Arrange(in RectF rect) {
		LayoutRect = rect;
		if (Visible)
			ArrangeCore(rect);
	}
	protected virtual void ArrangeCore(in RectF rect) {
	}

	public virtual void Render(Canvas cv, in UiRenderContext ctx) {
	}

	public virtual bool HitTest(Vector2 pos) => Visible && HitTestVisible && LayoutRect.Contains(pos);
}
