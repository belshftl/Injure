// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using Injure.DevAnalyzers.Attributes;
using Injure.Primitives;

namespace Injure.Ui;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UiAnchor {
	public enum Case {
		TopLeft = 1,
		Top,
		TopRight,
		Left,
		Center,
		Right,
		BottomLeft,
		Bottom,
		BottomRight,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UiSizingMode {
	public enum Case {
		Auto = 1,
		Explicit,
		Fill,
	}
}

public readonly record struct UiPlacement(
	UiAnchor Anchor,
	Vector2 Offset,
	UiSizingMode WidthMode,
	UiSizingMode HeightMode,
	float Width,
	float Height,
	UiThickness Margin = default
) {
	public static UiPlacement Center(SizeF size, Vector2 offset = default) =>
		new(UiAnchor.Center, offset, UiSizingMode.Explicit, UiSizingMode.Explicit, size.Width, size.Height);

	public static UiPlacement CenterAuto(Vector2 offset = default, UiThickness margin = default) =>
		new(UiAnchor.Center, offset, UiSizingMode.Auto, UiSizingMode.Auto, 0f, 0f, margin);

	public static UiPlacement AnchorAt(UiAnchor anchor, Vector2 offset, SizeF size, UiThickness margin = default) =>
		new(anchor, offset, UiSizingMode.Explicit, UiSizingMode.Explicit, size.Width, size.Height, margin);

	public static UiPlacement AnchorAtAuto(UiAnchor anchor, Vector2 offset = default, UiThickness margin = default) =>
		new(anchor, offset, UiSizingMode.Auto, UiSizingMode.Auto, 0f, 0f, margin);

	public static UiPlacement Fill(UiThickness margin = default) =>
		new(UiAnchor.TopLeft, default, UiSizingMode.Fill, UiSizingMode.Fill, 0f, 0f, margin);
}

public static class UiPlacementUtil {
	public static RectF ResolveChildRect(RectF parent, SizeF desired, UiPlacement p) {
		RectF area = parent.Deflate(p.Margin);
		float w = p.WidthMode.Tag switch {
			UiSizingMode.Case.Auto => desired.Width,
			UiSizingMode.Case.Explicit => p.Width,
			UiSizingMode.Case.Fill => area.Width,
			_ => throw new UnreachableException(),
		};
		float h = p.HeightMode.Tag switch {
			UiSizingMode.Case.Auto => desired.Height,
			UiSizingMode.Case.Explicit => p.Height,
			UiSizingMode.Case.Fill => area.Height,
			_ => throw new UnreachableException(),
		};

		w = MathF.Max(0f, w);
		h = MathF.Max(0f, h);

		Vector2 anchorPoint = getAnchorPoint(area, p.Anchor);
		Vector2 originOffset = getOriginOffset(new SizeF(w, h), p.Anchor);
		Vector2 pos = anchorPoint + p.Offset - originOffset;
		return new RectF(pos.X, pos.Y, w, h);
	}

	public static UiSizeConstraint GetChildMeasureConstraint(
		in UiSizeConstraint parentConstraint,
		in UiPlacement placement
	) {
		float maxW = getAxisMeasureConstraint(
			parentConstraint.IsWidthBounded,
			parentConstraint.MaxWidth,
			placement.Margin.Horizontal,
			placement.WidthMode,
			placement.Width
		);
		float maxH = getAxisMeasureConstraint(
			parentConstraint.IsHeightBounded,
			parentConstraint.MaxHeight,
			placement.Margin.Vertical,
			placement.HeightMode,
			placement.Height
		);
		return new UiSizeConstraint(maxW, maxH);
	}

	public static SizeF GetSurfaceDesiredSize(
		in UiSizeConstraint constraint,
		in UiPlacement placement,
		SizeF childDesired
	) {
		float w = constraint.IsWidthBounded
			? constraint.MaxWidth
			: getUnboundedSurfaceAxisSize(
				placement.WidthMode,
				placement.Width,
				placement.Margin.Horizontal,
				childDesired.Width
			);
		float h = constraint.IsHeightBounded
			? constraint.MaxHeight
			: getUnboundedSurfaceAxisSize(
				placement.HeightMode,
				placement.Height,
				placement.Margin.Vertical,
				childDesired.Height
			);
		return new SizeF(w, h);
	}

	private static Vector2 getAnchorPoint(RectF r, UiAnchor anchor) {
		return anchor.Tag switch {
			UiAnchor.Case.TopLeft => new Vector2(r.Left, r.Top),
			UiAnchor.Case.Top => new Vector2(r.CenterX, r.Top),
			UiAnchor.Case.TopRight => new Vector2(r.Right, r.Top),
			UiAnchor.Case.Left => new Vector2(r.Left, r.CenterY),
			UiAnchor.Case.Center => r.Center,
			UiAnchor.Case.Right => new Vector2(r.Right, r.CenterY),
			UiAnchor.Case.BottomLeft => new Vector2(r.Left, r.Bottom),
			UiAnchor.Case.Bottom => new Vector2(r.CenterX, r.Bottom),
			UiAnchor.Case.BottomRight => new Vector2(r.Right, r.Bottom),
			_ => throw new UnreachableException(),
		};
	}

	private static Vector2 getOriginOffset(SizeF s, UiAnchor anchor) {
		return anchor.Tag switch {
			UiAnchor.Case.TopLeft => Vector2.Zero,
			UiAnchor.Case.Top => new Vector2(s.Width * 0.5f, 0f),
			UiAnchor.Case.TopRight => new Vector2(s.Width, 0f),
			UiAnchor.Case.Left => new Vector2(0f, s.Height * 0.5f),
			UiAnchor.Case.Center => new Vector2(s.Width * 0.5f, s.Height * 0.5f),
			UiAnchor.Case.Right => new Vector2(s.Width, s.Height * 0.5f),
			UiAnchor.Case.BottomLeft => new Vector2(0f, s.Height),
			UiAnchor.Case.Bottom => new Vector2(s.Width * 0.5f, s.Height),
			UiAnchor.Case.BottomRight => new Vector2(s.Width, s.Height),
			_ => throw new UnreachableException(),
		};
	}

	private static float getAxisMeasureConstraint(
		bool parentBounded,
		float parentMax,
		float margin,
		UiSizingMode mode,
		float explicitSize
	) {
		return mode.Tag switch {
			UiSizingMode.Case.Explicit => MathF.Max(0f, explicitSize),
			UiSizingMode.Case.Auto or UiSizingMode.Case.Fill =>
				parentBounded ? MathF.Max(0f, parentMax - margin) : float.PositiveInfinity,
			_ => throw new UnreachableException(),
		};
	}

	private static float getUnboundedSurfaceAxisSize(
		UiSizingMode mode,
		float explicitSize,
		float margin,
		float childDesired
	) {
		float content = mode.Tag switch {
			UiSizingMode.Case.Explicit => explicitSize,
			UiSizingMode.Case.Auto or UiSizingMode.Case.Fill => childDesired,
			_ => throw new UnreachableException(),
		};
		return MathF.Max(0f, content + margin);
	}
}
