// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Primitives;

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

	internal static void Validate(in TextLayoutOptions options, string paramName) {
		ArgumentInvalidDefaultException.ThrowIfDefault(options, paramName);
		if (options.WrapMode == default)
			throw ArgumentInvalidDefaultException.For<TextWrapMode>($"{paramName}.WrapMode");
		if (options.HorizontalAlign == default)
			throw ArgumentInvalidDefaultException.For<TextHorizontalAlign>($"{paramName}.HorizontalAlign");
		if (!(options.MaxWidth >= 0f))
			throw new ArgumentOutOfRangeException(paramName, options.MaxWidth, "text layout max width must not be negative or NaN");
	}

	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// How a span of text looks: its fonts, size, color, and language.
/// </summary>
/// <remarks>
/// <para>
/// A plain string is laid out with a single style; a <see cref="RichText"/> gives each span of its
/// text its own. How the text is broken into lines is given separately, by
/// <see cref="TextLayoutOptions"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly record struct TextStyle : IHasInvalidDefault {
	/// <summary>
	/// Creates a text style.
	/// </summary>
	/// <param name="fonts">
	/// The fonts to lay the text out with. A <see cref="FontSpec"/>, <see cref="FontData"/>, or
	/// <see cref="Assets.Asset{T}"/> of <see cref="FontData"/> converts to this implicitly.
	/// </param>
	/// <param name="size">The size of one em in drawable units.</param>
	/// <param name="color">
	/// The color of the glyphs, or <see langword="null"/> for <see cref="SrgbColor32.White"/>.
	/// </param>
	/// <param name="language">
	/// The BCP 47 language tag of the text, or <c>"und"</c> (undetermined) to leave it unspecified;
	/// see <see cref="Language"/>.
	/// </param>
	public TextStyle(FontFallbackChain fonts, float size, SrgbColor32? color = null, string language = "und") {
		Fonts = fonts;
		Size = size;
		Color = color ?? SrgbColor32.White;
		Language = language;
	}

	/// <summary>
	/// The fonts to lay the text out with.
	/// </summary>
	public FontFallbackChain Fonts { get; init; }

	/// <summary>
	/// The size of one em in drawable units. Must be positive and finite.
	/// </summary>
	/// <remarks>
	/// Glyphs are rasterized for the effective scale when drawn, so text stays sharp at any
	/// <see cref="Canvas.PixelsPerUnit"/> or transform scale.
	/// </remarks>
	public float Size { get; init; }

	/// <summary>
	/// The color of the glyphs, which drawing multiplies by its tint.
	/// </summary>
	/// <remarks>
	/// A color change doesn't affect shaping, so text styled differently only in color shapes the
	/// same as if it weren't. A glyph that represents several characters, e.g. a ligature, gets the
	/// color of its first character.
	/// </remarks>
	public SrgbColor32 Color { get; init; }

	/// <summary>
	/// The BCP 47 language tag of the text, e.g. <c>"en"</c>, <c>"hu"</c>, <c>"zh-Hant"</c>,
	/// <c>"es-MX"</c>, etc; or <c>"und"</c> (undetermined) to leave it unspecified. Used for shaping
	/// and line breaking.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Shaping uses the whole tag to pick language-specific glyph forms. Line breaking follows the
	/// Unicode line breaking algorithm, which uses dictionaries for Thai, Lao, Khmer, and Myanmar text
	/// regardless of the tag; the tag affects it through these parts only:
	/// <list type="bullet">
	/// <item><description>
	/// The Unicode extension keyword <c>lb</c> selects the strictness of CJK line breaking.
	/// <c>strict</c> (the default) doesn't break before small kana, prolonged sound marks, iteration
	/// marks, and similar characters. <c>normal</c> also breaks before small kana and prolonged sound
	/// marks, and <c>loose</c> also before iteration marks and hyphens like U+2010 HYPHEN. For
	/// Japanese and Chinese (primary language subtag <c>ja</c> or <c>zh</c>, case-insensitive),
	/// <c>normal</c> and <c>loose</c> also break before U+301C WAVE DASH and U+30A0
	/// KATAKANA-HIRAGANA DOUBLE HYPHEN, and <c>loose</c> also before some postfix symbols and
	/// punctuation (e.g. U+00B0 DEGREE SIGN, U+FF05 FULLWIDTH PERCENT SIGN, U+FF01 FULLWIDTH
	/// EXCLAMATION MARK) and after some prefix symbols (e.g. U+FFE5 FULLWIDTH YEN SIGN). Example:
	/// <c>"ja-u-lb-normal"</c>.
	/// </description></item>
	/// <item><description>
	/// The Unicode extension keyword <c>lw</c> selects where lines may break within words:
	/// <c>normal</c> (the default) = per the algorithm, <c>keepall</c> = never within words,
	/// including CJK text, so e.g. Korean only breaks at spaces, and <c>breakall</c> = between any
	/// two letters. Example: <c>"ko-u-lw-keepall"</c>.
	/// </description></item>
	/// </list>
	/// Other languages have no specific line breaking rules. Whether a line may break after a
	/// character follows the language of that character.
	/// </para>
	/// <para>
	/// An unspecified language (<c>"und"</c>), or an unrecognized one, uses language-neutral
	/// shaping / line breaking. Malformed tags are not rejected.
	/// </para>
	/// </remarks>
	public string Language { get; init; }

	internal static void Validate(in TextStyle style, string paramName) {
		ArgumentInvalidDefaultException.ThrowIfDefault(style, paramName);
		if (style.Fonts is null)
			throw new ArgumentNullException($"{paramName}.Fonts");
		if (style.Language is null)
			throw new ArgumentNullException($"{paramName}.Language");
		if (!(style.Size > 0f) || !float.IsFinite(style.Size))
			throw new ArgumentOutOfRangeException($"{paramName}.Size", style.Size, "text size must be positive and finite");
	}

	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// Changes to some of a <see cref="TextStyle"/>'s values, for <see cref="RichTextBuilder"/>.
/// </summary>
/// <param name="Fonts">Replaces <see cref="TextStyle.Fonts"/>.</param>
/// <param name="Size">Replaces <see cref="TextStyle.Size"/>.</param>
/// <param name="Color">Replaces <see cref="TextStyle.Color"/>.</param>
/// <param name="Language">Replaces <see cref="TextStyle.Language"/>.</param>
/// <remarks>
/// <para>
/// A <see langword="null"/> field keeps the current value.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and doesn't change anything.
/// </para>
/// </remarks>
public readonly record struct TextStyleOverride(
	FontFallbackChain? Fonts = null,
	float? Size = null,
	SrgbColor32? Color = null,
	string? Language = null
) {
	internal TextStyle ApplyTo(in TextStyle style) => new(
		Fonts ?? style.Fonts,
		Size ?? style.Size,
		Color ?? style.Color,
		Language ?? style.Language
	);
}
