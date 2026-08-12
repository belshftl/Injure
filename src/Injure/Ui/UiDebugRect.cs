// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw;
using Injure.Primitives;

namespace Injure.Ui;

public sealed class UiDebugRect : UiWidget {
	public Color32 Fill { get; set; }
	public Color32? Stroke { get; set; }
	public float StrokeWidth { get; set; } = 1f;
	public SizeF PreferredSize { get; set; }

	public UiDebugRect(Color32 fill, SizeF preferredSize = default) {
		Fill = fill;
		PreferredSize = preferredSize;
	}

	protected override SizeF MeasureCore(in UiLayoutContext ctx, in UiSizeConstraint constraint) {
		float w = PreferredSize.Width;
		float h = PreferredSize.Height;

		if (w <= 0f)
			w = constraint.IsWidthBounded ? constraint.MaxWidth : 64f;
		if (h <= 0f)
			h = constraint.IsHeightBounded ? constraint.MaxHeight : 64f;

		if (constraint.IsWidthBounded)
			w = MathF.Min(w, constraint.MaxWidth);
		if (constraint.IsHeightBounded)
			h = MathF.Min(h, constraint.MaxHeight);

		return new SizeF(MathF.Max(0f, w), MathF.Max(0f, h));
	}

	public override void Render(Canvas cv, in UiRenderContext ctx) {
		if (!Visible)
			return;
		if (Stroke is Color32 stroke && StrokeWidth > 0f)
			cv.Rect(LayoutRect.Inflate(new UiThickness(StrokeWidth)), stroke);
		cv.Rect(LayoutRect, Fill);
	}
}
