// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.DevAnalyzers.Attributes;
using Injure.Primitives;

namespace Injure.Ui;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UiCanvasMode {
	public enum Case {
		Fixed = 1,
		FixedHeightExpandWidth,
		FixedWidthExpandHeight,
		MatchDrawable,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UiCanvasFitMode {
	public enum Case {
		Letterbox = 1,
		Stretch,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct UiCanvasScaleMode {
	public enum Case {
		Fractional = 1,
		Integer,
	}
}

public readonly record struct UiCanvasPolicy(
	UiCanvasMode Mode,
	SizeF ReferenceSize,
	UiCanvasFitMode FitMode,
	UiCanvasScaleMode ScaleMode
) {
	public UiCanvasPolicy(UiCanvasMode Mode, SizeF ReferenceSize) : this(Mode, ReferenceSize, UiCanvasFitMode.Letterbox, UiCanvasScaleMode.Fractional) {}

	public static UiCanvasPolicy Fixed(float width, float height) => new(UiCanvasMode.Fixed, new SizeF(width, height));
	public static UiCanvasPolicy FixedHeight(float width, float height) => new(UiCanvasMode.FixedHeightExpandWidth, new SizeF(width, height));
	public static UiCanvasPolicy FixedWidth(float width, float height) => new(UiCanvasMode.FixedWidthExpandHeight, new SizeF(width, height));
	public static readonly UiCanvasPolicy MatchDrawable = new(
		UiCanvasMode.MatchDrawable,
		new SizeF(1f, 1f),
		UiCanvasFitMode.Stretch,
		UiCanvasScaleMode.Fractional
	);
}

public readonly record struct UiCanvasTransform(
	RectF LogicalRect,
	RectI ViewportRect,
	Vector2 Scale
) {
	public float TextScale => MathF.Min(Scale.X, Scale.Y);
}
