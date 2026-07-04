// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw;
using Injure.Primitives;

namespace Injure.Ui;

public sealed class UiOverlay : UiWidget {
	private readonly List<UiWidget> children = new();
	public override IReadOnlyList<UiWidget> Children => children;

	public void Add(UiWidget child) {
		ArgumentNullException.ThrowIfNull(child);
		child.AttachToParent(this);
		children.Add(child);
	}

	protected override SizeF MeasureCore(in UiLayoutContext ctx, in UiSizeConstraint constraint) {
		float w = constraint.IsWidthBounded ? constraint.MaxWidth : 0f;
		float h = constraint.IsHeightBounded ? constraint.MaxHeight : 0f;

		foreach (UiWidget child in children) {
			SizeF s = child.Measure(in ctx, constraint);
			if (!constraint.IsWidthBounded)
				w = MathF.Max(w, s.Width);
			if (!constraint.IsHeightBounded)
				h = MathF.Max(h, s.Height);
		}

		return new SizeF(w, h);
	}

	protected override void ArrangeCore(in RectF rect) {
		foreach (UiWidget child in children)
			child.Arrange(rect);
	}

	public override void Render(Canvas cv, in UiRenderContext ctx) {
		foreach (UiWidget child in children)
			if (child.Visible)
				child.Render(cv, in ctx);
	}
}
