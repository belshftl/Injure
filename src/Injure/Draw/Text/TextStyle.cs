// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Primitives;

namespace Injure.Draw.Text;

/// <summary>
/// Text line-breaking, alignment, and truncation options.
/// </summary>
/// <remarks>
/// <para>
/// Constructed using a <see langword="with"/> expression on <see cref="Default"/>, e.g.
/// <c>TextLayoutOptions.Default with { MaxWidth = 200, MaxLines = 2 }</c>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly record struct TextLayoutOptions : IHasInvalidDefault {
	/// <summary>
	/// Default/neutral-ish options:
	/// <list type="bullet">
	/// <item><description>
	/// a <see cref="MaxWidth"/> of <see cref="float.PositiveInfinity"/>;
	/// </description></item>
	/// <item><description>
	/// <see cref="TextWrapMode.Greedy"/> wrapping;
	/// </description></item>
	/// <item><description>
	/// <see cref="TextHorizontalAlign.Start"/> alignment;
	/// </description></item>
	/// <item><description>
	/// no <see cref="MaxLines"/> (left at <see langword="null"/>);
	/// </description></item>
	/// <item><description>
	/// a <see cref="MaxHeight"/> of <see cref="float.PositiveInfinity"/>;
	/// </description></item>
	/// <item><description>
	/// <see cref="TextHorizontalOverflow.Overflow"/>;
	/// </description></item>
	/// <item><description>
	/// and <see cref="TextTruncationMarker.Ellipsis"/>.
	/// </description></item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// <para>
	/// The wrapping is <see cref="TextWrapMode.Greedy"/> even though the max width is infinite, so
	/// that manually setting a finite <see cref="MaxWidth"/>, e.g.
	/// <c>TextLayoutOptions.Default with { MaxWidth = 200 }</c>, wraps text at it. With the infinite
	/// <see cref="MaxWidth"/>, lines only break at explicit line breaks.
	/// </para>
	/// <para>
	/// The truncation marker has no effect unless text is truncated, which doesn't happen at the
	/// default values, and requires either setting a max lines/height cap, or a configuration that
	/// truncates overlong lines and allows them to happen in the first place.
	/// </para>
	/// </remarks>
	public static TextLayoutOptions Default { get; } = new() {
		MaxWidth = float.PositiveInfinity,
		MaxHeight = float.PositiveInfinity,
		WrapMode = TextWrapMode.Greedy,
		HorizontalAlign = TextHorizontalAlign.Start,
		HorizontalOverflow = TextHorizontalOverflow.Overflow,
		TruncationMarker = TextTruncationMarker.Ellipsis,
	};

	/// <summary>
	/// Width of the box the text is laid out in, in drawable units, which
	/// <see cref="TextWrapMode.Greedy"/> wraps lines at and <see cref="HorizontalAlign"/> aligns them
	/// within. May be <see cref="float.PositiveInfinity"/> to not wrap any lines and use the widest
	/// line's width as the width of the box used for alignment. Must not be negative or NaN.
	/// </summary>
	public float MaxWidth { get; init; }

	/// <summary>
	/// How lines wrap beyond explicit line breaks.
	/// </summary>
	public TextWrapMode WrapMode { get; init; }

	/// <summary>
	/// How lines are aligned within the box, including lines that overflow it; see
	/// <see cref="TextHorizontalAlign"/>.
	/// </summary>
	public TextHorizontalAlign HorizontalAlign { get; init; }

	/// <summary>
	/// The most lines to lay out, or <see langword="null"/> for no limit. Must be at least 1 if not
	/// <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// Lines past the limit are dropped, and the last kept line is truncated (see
	/// <see cref="TextLine.IsTruncated"/>), even if all of the lines that got dropped were empty. If
	/// <see cref="MaxHeight"/> is finite, the first reached limit wins.
	/// </remarks>
	public int? MaxLines { get; init; }

	/// <summary>
	/// The most vertical space the lines may take up, in drawable units. May be
	/// <see cref="float.PositiveInfinity"/> for no limit. Must not be negative or NaN.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A line fits if its bottom, i.e. <see cref="TextLine.BaselineY"/> +
	/// <see cref="TextLine.Descent"/>, is at most this. The lines from the first one that doesn't fit
	/// onward are dropped, and the last kept line is truncated (see
	/// <see cref="TextLine.IsTruncated"/>), even if all of the lines that got dropped were empty. If
	/// <see cref="MaxLines"/> is non-<see langword="null"/>, the first reached limit wins.
	/// </para>
	/// <para>
	/// The first line is always kept, even if it doesn't fit. This is so that text never lays out to
	/// no lines, but could be surprising if this is used as a "never overflow this box" knob.
	/// </para>
	/// </remarks>
	public float MaxHeight { get; init; }

	/// <summary>
	/// What happens to lines wider than the box.
	/// </summary>
	public TextHorizontalOverflow HorizontalOverflow { get; init; }

	/// <summary>
	/// Marker appended to truncated lines.
	/// </summary>
	public TextTruncationMarker TruncationMarker { get; init; }

	internal static void Validate(in TextLayoutOptions options, string paramName) {
		ArgumentInvalidDefaultException.ThrowIfDefault(options, paramName);
		if (options.WrapMode == default)
			throw ArgumentInvalidDefaultException.For<TextWrapMode>($"{paramName}.WrapMode");
		if (options.HorizontalAlign == default)
			throw ArgumentInvalidDefaultException.For<TextHorizontalAlign>($"{paramName}.HorizontalAlign");
		if (options.HorizontalOverflow == default)
			throw ArgumentInvalidDefaultException.For<TextHorizontalOverflow>($"{paramName}.HorizontalOverflow");
		if (options.TruncationMarker == default)
			throw ArgumentInvalidDefaultException.For<TextTruncationMarker>($"{paramName}.TruncationMarker");
		if (!(options.MaxWidth >= 0f))
			throw new ArgumentOutOfRangeException($"{paramName}.MaxWidth", options.MaxWidth, "text layout max width must not be negative or NaN");
		if (options.MaxLines < 1)
			throw new ArgumentOutOfRangeException($"{paramName}.MaxLines", options.MaxLines, "text layout max lines must be at least 1");
		if (!(options.MaxHeight >= 0f))
			throw new ArgumentOutOfRangeException($"{paramName}.MaxHeight", options.MaxHeight, "text layout max height must not be negative or NaN");
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
	/// The color of the glyphs, or <see langword="null"/> for <see cref="SrgbStraight32.White"/>.
	/// </param>
	/// <param name="language">
	/// The BCP 47 language tag of the text, or <c>"und"</c> (undetermined) to leave it unspecified;
	/// see <see cref="Language"/>.
	/// </param>
	public TextStyle(FontFallbackChain fonts, float size, SrgbStraight32? color = null, string language = "und") {
		Fonts = fonts;
		Size = size;
		Color = color ?? SrgbStraight32.White;
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
	public SrgbStraight32 Color { get; init; }

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
	SrgbStraight32? Color = null,
	string? Language = null
) {
	internal TextStyle ApplyTo(in TextStyle style) => new(
		Fonts ?? style.Fonts,
		Size ?? style.Size,
		Color ?? style.Color,
		Language ?? style.Language
	);
}
