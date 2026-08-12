// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Injure.Draw;
using Injure.Draw.Text;
using Injure.Primitives;

namespace Injure.Ui;

public sealed class UiLabel : UiWidget {
	private readonly TextSystem textSystem;
	private string value = string.Empty;
	private LiveText? live;
	private string? liveTextValue;
	private TextStyle liveStyle;

	public string Text {
		get => value;
		set => this.value = value ?? string.Empty;
	}

	public UiTextStyle Style { get; set; }

	public UiLabel(TextSystem textSystem, UiTextStyle style, string text) {
		this.textSystem = textSystem ?? throw new ArgumentNullException(nameof(textSystem));
		Style = style;
		Text = text;
	}

	protected override SizeF MeasureCore(in UiLayoutContext ctx, in UiSizeConstraint constraint) {
		float maxLogicalWidth = constraint.MaxWidth;
		TextStyle engineStyle = UiTextStyleUtil.ToEngineStyle(Style, ctx.TextScale, maxLogicalWidth);
		TextMeasurement m = textSystem.Measure(Style.Fonts, Text, in engineStyle);

		return constraint.Clamp(
			new SizeF(
				m.Width / ctx.TextScale + Padding.Horizontal,
				m.Height / ctx.TextScale + Padding.Vertical
			)
		);
	}

	public override void Render(Canvas cv, in UiRenderContext ctx) {
		if (!Visible)
			return;

		RectF inner = LayoutRect.Deflate(Padding);
		TextStyle engineStyle = UiTextStyleUtil.ToEngineStyle(Style, ctx.TextScale, inner.Width);
		ensure(engineStyle);
		Vector2 targetAt = ctx.LogicalToTarget(inner.Position);
		using (cv.PushParams(new CanvasParamsOverride(Transform: Matrix3x2.Identity)))
			live.Render(cv, targetAt);
	}

	[MemberNotNull(nameof(live))]
	private void ensure(TextStyle engineStyle) {
		if (live is null) {
			live = textSystem.Make(Style.Fonts, Text, engineStyle);
			liveTextValue = Text;
			liveStyle = engineStyle;
			return;
		}
		if (!StringComparer.Ordinal.Equals(liveTextValue, Text) || !liveStyle.Equals(engineStyle)) {
			live.SetParams(Style.Fonts, Text, engineStyle);
			liveTextValue = Text;
			liveStyle = engineStyle;
		}
	}
}
