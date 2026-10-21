// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Draw.Text;

/// <summary>
/// Text line-breaking/alignment options.
/// </summary>
/// <param name="MaxWidth">
/// Width of the box the text is laid out in, in drawable units, which
/// <see cref="TextWrapMode.Greedy"/> wraps lines at and <see cref="HorizontalAlign"/> aligns them
/// within. May be <see cref="float.PositiveInfinity"/> to not wrap any lines and use the widest
/// line's width as the width of the box used for alignment. Must not be negative or NaN.
/// </param>
/// <param name="WrapMode">How lines wrap beyond explicit line breaks.</param>
/// <param name="HorizontalAlign">
/// How lines are aligned within the box, including lines that overflow it; see
/// <see cref="TextHorizontalAlign"/>.
/// </param>
/// <remarks>
/// <para>
/// A <see langword="with"/> expression can be used with <see cref="Default"/> to only change some
/// options, e.g.
/// <c>TextLayoutOptions.Default with { HorizontalAlign = TextHorizontalAlign.Center }</c>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly record struct TextLayoutOptions(
	float MaxWidth,
	TextWrapMode WrapMode,
	TextHorizontalAlign HorizontalAlign
) : IHasInvalidDefault {
	/// <summary>
	/// Default/neutral-ish options; a <see cref="MaxWidth"/> of <see cref="float.PositiveInfinity"/>,
	/// <see cref="TextWrapMode.Greedy"/> wrapping, and <see cref="TextHorizontalAlign.Left"/>
	/// alignment.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The wrapping is <see cref="TextWrapMode.Greedy"/> even though the max width is infinite, so
	/// that manually setting a finite <see cref="MaxWidth"/>, e.g.
	/// <c>TextLayoutOptions.Default with { MaxWidth = 200 }</c>, wraps text at it. With the infinite
	/// <see cref="MaxWidth"/>, lines only break at explicit line breaks.
	/// </para>
	/// <para>
	/// <see cref="TextHorizontalAlign.Left"/> is "neutral" because it's the natural alignment from
	/// not adding an X offset, and in practice only looks neutral for LTR text.
	/// </para>
	/// </remarks>
	public static TextLayoutOptions Default { get; } =
		new(float.PositiveInfinity, TextWrapMode.Greedy, TextHorizontalAlign.Left);

	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// How text is sized and laid out. Color is given when drawing instead.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct TextStyle : IHasInvalidDefault {
	/// <summary>
	/// Creates a text style.
	/// </summary>
	/// <param name="size">The size of one em in drawable units.</param>
	/// <param name="layout">
	/// Line-breaking/alignment options, or <see langword="null"/> for
	/// <see cref="TextLayoutOptions.Default"/>.
	/// </param>
	/// <param name="language">
	/// The BCP 47 language tag of the text, or <see langword="null"/> to leave it unspecified; see
	/// <see cref="Language"/>.
	/// </param>
	public TextStyle(float size, TextLayoutOptions? layout = null, string? language = null) {
		Size = size;
		Layout = layout ?? TextLayoutOptions.Default;
		Language = language;
	}

	/// <summary>
	/// The size of one em in drawable units. Must be positive and finite.
	/// </summary>
	/// <remarks>
	/// Glyphs are rasterized for the effective scale when drawn, so text stays sharp at any
	/// <see cref="Canvas.PixelsPerUnit"/> or transform scale.
	/// </remarks>
	public float Size { get; init; }

	/// <summary>
	/// Line-breaking/alignment options.
	/// </summary>
	public TextLayoutOptions Layout { get; init; }

	/// <summary>
	/// The BCP 47 language tag of the text, e.g. <c>"en"</c>, <c>"hu"</c>, <c>"zh-Hant"</c>,
	/// <c>"es-MX"</c>, etc; or <see langword="null"/> to leave it unspecified. Used for shaping and
	/// line breaking.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Shaping uses the whole tag to pick language-specific glyph forms. Line breaking uses the
	/// primary language subtag (case-insensitive) to apply language-specific rules for the languages
	/// that have them, and the Unicode extension <c>-u-lb-strict</c> (e.g. <c>"ja-u-lb-strict"</c>) to
	/// break CJK text strictly, i.e. not before small kana and similar characters.
	/// </para>
	/// <para>
	/// An unspecified language (<see langword="null"/>), or an unrecognized one, uses
	/// language-neutral shaping / line breaking. Malformed tags are not rejected.
	/// </para>
	/// </remarks>
	public string? Language { get; init; }

	internal static void Validate(in TextStyle style, string paramName) {
		ArgumentInvalidDefaultException.ThrowIfDefault(style, paramName);
		if (!(style.Size > 0f) || !float.IsFinite(style.Size))
			throw new ArgumentOutOfRangeException(paramName, style.Size, "text size must be positive and finite");
		TextLayoutOptions layout = style.Layout;
		if (layout.WrapMode == default)
			throw ArgumentInvalidDefaultException.For<TextWrapMode>($"{paramName}.Layout.WrapMode");
		if (layout.HorizontalAlign == default)
			throw ArgumentInvalidDefaultException.For<TextHorizontalAlign>($"{paramName}.Layout.HorizontalAlign");
		if (!(layout.MaxWidth >= 0f))
			throw new ArgumentOutOfRangeException(paramName, layout.MaxWidth, "text layout max width must not be negative or NaN");
	}

	bool IHasInvalidDefault.IsDefault => this == default;
}
