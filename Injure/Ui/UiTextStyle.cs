// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;
using Injure.Primitives;

namespace Injure.Ui;

public readonly record struct UiTextStyle(
	FontFallbackChain Fonts,
	float Size,
	Color32 Color,
	TextWrapMode WrapMode = default,
	TextHorizontalAlign HorizontalAlign = default,
	string Locale = "und",
	string? LanguageBCP47 = null,
	FontRasterMode RasterMode = default,
	FontHinting Hinting = default,
	bool UseEmbeddedBitmaps = true
);

public static class UiTextStyleUtil {
	public static int ResolvePixelSize(UiTextStyle style, float textScale) {
		if (!float.IsFinite(textScale) || textScale <= 0f)
			textScale = 1f;
		return Math.Max(1, (int)MathF.Round(style.Size * textScale));
	}

	public static TextStyle ToEngineStyle(UiTextStyle style, float textScale, float maxLogicalWidth) {
		int px = ResolvePixelSize(style, textScale);
		float maxPx = float.IsPositiveInfinity(maxLogicalWidth) ? float.PositiveInfinity : MathF.Max(0f, maxLogicalWidth * textScale);
		return new TextStyle(
			new FontOptions(px, style.RasterMode, style.Hinting, style.UseEmbeddedBitmaps),
			style.Color,
			new TextLayoutOptions(maxPx, style.WrapMode, style.HorizontalAlign),
			style.Locale,
			style.LanguageBCP47
		);
	}
}

public sealed class UiTheme {
	public required UiTextStyle BodyText { get; init; }
	public required UiTextStyle ButtonText { get; init; }
	public required UiTextStyle HeadingText { get; init; }
}
