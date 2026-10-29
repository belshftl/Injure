// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Text;

namespace Injure.Draw.Text;

/// <summary>
/// A span of a <see cref="RichText"/> and its style.
/// </summary>
/// <param name="Start">Index of the span's first UTF-16 code unit in <see cref="RichText.Text"/>.</param>
/// <param name="Length">Length of the span in UTF-16 code units.</param>
/// <param name="Style">The span's style.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct RichTextSpan(
	int Start,
	int Length,
	TextStyle Style
) : IHasInvalidDefault {
	/// <summary>
	/// Index just past the span's last UTF-16 code unit.
	/// </summary>
	public int Limit => Start + Length;

	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// Immutable text with per-span style, built with <see cref="RichTextBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Spans"/> covers <see cref="Text"/> in order without gaps, and adjacent spans have
/// different styles. Empty text has a single empty span, whose style sets the metrics of the
/// layout's one empty line.
/// </para>
/// <para>
/// Unpaired surrogates in the text are replaced with U+FFFD REPLACEMENT CHARACTER. U+FFFD is a
/// single UTF-16 code unit, just like an unpaired surrogate, so the length of the text and
/// positions in it remain unchanged. A surrogate pair is never split between spans; if two halves
/// get appended with different styles, the first half's is used for the whole pair.
/// </para>
/// <para>
/// Two <see cref="RichText"/> are equal if their texts and spans are equal, so one that's built
/// again with the same contents every frame still hits the cache of
/// <see cref="Canvas.Text(RichText, System.Numerics.Vector2, TextLayoutOptions?, Primitives.SrgbStraight32?)"/>.
/// </para>
/// </remarks>
public sealed class RichText : IEquatable<RichText> {
	private readonly ImmutableArray<RichTextSpan> spans;
	private readonly int hash;

	/// <summary>
	/// Creates rich text with a single style.
	/// </summary>
	/// <param name="text">
	/// The text. Paragraphs and lines are separated as described on <see cref="TextSystem"/>. Unpaired
	/// surrogates are replaced with U+FFFD REPLACEMENT CHARACTER (see type-level docs).
	/// </param>
	/// <param name="style">The style of the whole text.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="text"/>, or <paramref name="style"/>'s
	/// <see cref="TextStyle.Fonts"/>, is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="style"/>'s <see cref="TextStyle.Size"/> isn't positive and finite.
	/// </exception>
	public RichText(string text, in TextStyle style) {
		ArgumentNullException.ThrowIfNull(text);
		TextStyle.Validate(in style, nameof(style));
		Text = TextAnalysis.ReplaceUnpairedSurrogates(text);
		spans = [new RichTextSpan(0, Text.Length, style)];
		hash = computeHash();
	}

	private RichText(string text, ImmutableArray<RichTextSpan> spans) {
		Text = text;
		this.spans = spans;
		hash = computeHash();
	}
	internal static RichText CreateTrusted(string text, ImmutableArray<RichTextSpan> spans) => new(text, spans);

	/// <summary>
	/// The text, with unpaired surrogates replaced with U+FFFD REPLACEMENT CHARACTER.
	/// </summary>
	public string Text { get; }

	/// <summary>
	/// The styled spans, in order.
	/// </summary>
	public IReadOnlyList<RichTextSpan> Spans => spans;
	internal ReadOnlySpan<RichTextSpan> SpansSpan => spans.AsSpan();

	internal int SpanIndexAt(int index) {
		ReadOnlySpan<RichTextSpan> s = spans.AsSpan();
		int lo = 0, hi = s.Length - 1;
		while (lo < hi) {
			int mid = lo + (hi - lo + 1) / 2;
			if (s[mid].Start <= index)
				lo = mid;
			else
				hi = mid - 1;
		}
		return lo;
	}

	private int computeHash() {
		HashCode h = new();
		h.Add(Text, StringComparer.Ordinal);
		foreach (RichTextSpan span in spans)
			h.Add(span);
		return h.ToHashCode();
	}

	/// <summary>
	/// Checks whether this and <paramref name="other"/> have the same text and spans.
	/// </summary>
	public bool Equals(RichText? other) =>
		other is not null
		&& (
			ReferenceEquals(this, other)
			|| hash == other.hash && Text == other.Text && spans.AsSpan().SequenceEqual(other.spans.AsSpan())
		);

	/// <summary>
	/// Checks whether this and <paramref name="obj"/> have the same text and spans, if
	/// <paramref name="obj"/> is a <see cref="RichText"/>.
	/// </summary>
	public override bool Equals(object? obj) => Equals(obj as RichText);

	/// <summary>
	/// Gets a structural hash in correspondence with <see cref="Equals(RichText?)"/>.
	/// </summary>
	public override int GetHashCode() => hash;

	public static bool operator ==(RichText? left, RichText? right) => left is null ? right is null : left.Equals(right);
	public static bool operator !=(RichText? left, RichText? right) => !(left == right);
}

/// <summary>
/// Builds <see cref="RichText"/> by appending text with style changes handled by a stack-based
/// <see cref="Push(in TextStyleOverride)"/> and <see cref="Pop()"/>.
/// </summary>
/// <remarks>
/// <para>
/// A stack of <see cref="TextStyle"/>s ("the style stack") is kept; appending text uses the top of
/// that stack, and it can be manipulated via <see cref="Push(in TextStyleOverride)"/> and
/// <see cref="Pop()"/>, similar to <see cref="Canvas"/>. For example:
/// <code>
/// RichText t = new RichTextBuilder(baseStyle)
/// 	.Append("HP: ")
/// 	.Push(new TextStyleOverride(Color: SrgbStraight32.Red))
/// 	.Append("12")
/// 	.Pop()
/// 	.Append(" / 25")
/// 	.Build();
/// </code>
/// </para>
/// <para>
/// <see cref="Build()"/> doesn't reset the builder, so it can keep appending and build again.
/// </para>
/// <para>
/// Not thread-safe.
/// </para>
/// </remarks>
public sealed class RichTextBuilder {
	private readonly StringBuilder sb = new();
	private readonly List<RichTextSpan> spans = new();
	private readonly TextStyle baseStyle;
	/// <summary>
	/// The "base of the style stack" is actually <see cref="baseStyle"/>; this only holds the
	/// additional styles pushed by <see cref="Push(in TextStyleOverride)"/>.
	/// </summary>
	private readonly Stack<TextStyle> styleStack = new();

	/// <summary>
	/// The base of the style stack, which can't be popped off.
	/// </summary>
	public TextStyle BaseStyle => baseStyle;

	/// <summary>
	/// The top of the style stack, i.e. the currently active text style for appends.
	/// </summary>
	public TextStyle CurrentStyle => styleStack.TryPeek(out TextStyle s) ? s : baseStyle;

	/// <summary>
	/// Creates a builder.
	/// </summary>
	/// <param name="baseStyle">The base of the style stack, which can't be popped off.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="baseStyle"/>'s <see cref="TextStyle.Fonts"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="baseStyle"/>'s <see cref="TextStyle.Size"/> isn't positive and
	/// finite.
	/// </exception>
	public RichTextBuilder(in TextStyle baseStyle) {
		TextStyle.Validate(in baseStyle, nameof(baseStyle));
		this.baseStyle = baseStyle;
	}

	/// <summary>
	/// Pushes an override onto the style stack, setting a new text style for appends.
	/// </summary>
	/// <param name="ov">Partial override applied on top of <see cref="CurrentStyle"/>.</param>
	/// <returns>This builder.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="ov"/>'s <see cref="TextStyleOverride.Size"/> isn't positive and
	/// finite.
	/// </exception>
	public RichTextBuilder Push(in TextStyleOverride ov) {
		TextStyle next = ov.ApplyTo(CurrentStyle);
		TextStyle.Validate(in next, nameof(ov));
		styleStack.Push(next);
		return this;
	}

	/// <summary>
	/// Pops off the top of the style stack and discards it, restoring the text style before the last
	/// <see cref="Push(in TextStyleOverride)"/>.
	/// </summary>
	/// <returns>This builder.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the pop would pop off the base of the style stack, i.e. there's no push to undo.
	/// </exception>
	public RichTextBuilder Pop() {
		if (!styleStack.TryPop(out _))
			throw new InvalidOperationException("style stack has only the base, i.e. there's no matching Push() for this Pop()");
		return this;
	}

	/// <summary>
	/// Appends text with the current style.
	/// </summary>
	/// <param name="text">
	/// The text to append. Paragraphs and lines are separated as described on
	/// <see cref="TextSystem"/>. Unpaired surrogates are replaced with U+FFFD REPLACEMENT CHARACTER.
	/// </param>
	/// <returns>This builder.</returns>
	public RichTextBuilder Append(ReadOnlySpan<char> text) {
		if (text.IsEmpty)
			return this;
		// Build() merges adjacent spans with equal styles
		spans.Add(new RichTextSpan(sb.Length, text.Length, CurrentStyle));
		sb.Append(text);
		return this;
	}

	/// <inheritdoc cref="Append(ReadOnlySpan{char})"/>
	public RichTextBuilder Append(string text) => Append(text.AsSpan());

	/// <summary>
	/// Appends text with <paramref name="style"/> applied to the current style, like a
	/// <see cref="Push(in TextStyleOverride)"/>, <see cref="Append(ReadOnlySpan{char})"/>, and
	/// <see cref="Pop()"/>.
	/// </summary>
	/// <param name="text">
	/// The text to append. Paragraphs and lines are separated as described on
	/// <see cref="TextSystem"/>. Unpaired surrogates are replaced with U+FFFD REPLACEMENT CHARACTER.
	/// </param>
	/// <param name="style">Partial override applied on top of <see cref="CurrentStyle"/>.</param>
	/// <returns>This builder.</returns>
	/// <inheritdoc cref="Push(in TextStyleOverride)" path="/exception"/>
	public RichTextBuilder Append(ReadOnlySpan<char> text, in TextStyleOverride style) =>
		Push(in style).Append(text).Pop();

	/// <inheritdoc cref="Append(ReadOnlySpan{char}, in TextStyleOverride)"/>
	public RichTextBuilder Append(string text, in TextStyleOverride style) => Append(text.AsSpan(), in style);

	/// <summary>
	/// Builds the rich text appended so far; doesn't reset the builder, so it can keep appending
	/// and build again.
	/// </summary>
	/// <returns>The built rich text.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the style stack still has anything on it other than the base, i.e. there's some
	/// <see cref="Push(in TextStyleOverride)"/> with no matching <see cref="Pop()"/>.
	/// </exception>
	public RichText Build() {
		if (styleStack.Count != 0)
			throw new InvalidOperationException($"style stack still has {styleStack.Count} more styles atop the base, i.e. that many unclosed Push()es without matching Pop()s");
		string s = TextAnalysis.ReplaceUnpairedSurrogates(sb.ToString());
		if (spans.Count == 0)
			return RichText.CreateTrusted(s, [new RichTextSpan(0, 0, baseStyle)]);
		ImmutableArray<RichTextSpan>.Builder result = ImmutableArray.CreateBuilder<RichTextSpan>(spans.Count);
		foreach (RichTextSpan span in spans) {
			int start = span.Start, limit = span.Limit;
			// move a span boundary inside a surrogate pair past the pair
			if (start > 0 && char.IsLowSurrogate(s[start]) && char.IsHighSurrogate(s[start - 1]))
				start++;
			if (limit < s.Length && char.IsLowSurrogate(s[limit]) && char.IsHighSurrogate(s[limit - 1]))
				limit++;
			if (limit <= start)
				continue;
			if (result.Count > 0 && result[^1].Style == span.Style)
				result[^1] = result[^1] with { Length = limit - result[^1].Start };
			else
				result.Add(new RichTextSpan(start, limit - start, span.Style));
		}
		return RichText.CreateTrusted(s, result.DrainToImmutable());
	}
}
