// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarfBuzzSharp;
using Injure.DevAnalyzers.Attributes;
using Injure.Primitives;

namespace Injure.Draw.Text;

/// <summary>
/// How text is broken into lines beyond explicit line breaks.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TextWrapMode {
	/// <summary>Raw switch tag for <see cref="TextWrapMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Lines only break at explicit line breaks.
		/// </summary>
		None = 1,

		/// <summary>
		/// Lines also break at the last break opportunity (e.g. a space) that keeps them within
		/// <see cref="TextLayoutOptions.MaxWidth"/>, or within a word if a single word is wider. Spaces
		/// at the end of a wrapped line don't count towards its width.
		/// </summary>
		/// <remarks>
		/// A line that breaks at a soft hyphen (U+00AD) ends in a hyphen, which counts towards its
		/// width. The hyphen has the style of the soft hyphen character, and is drawn as U+2010 HYPHEN
		/// from the style's primary font, or <c>'-'</c> in said font if it lacks U+2010. If the primary
		/// font lacks <c>'-'</c> too, then <c>'-'</c> is tried going through the rest of the fallback
		/// chain. Soft hyphens in any other position are not drawn.
		/// </remarks>
		Greedy,
	}
}

/// <summary>
/// How lines are positioned horizontally within the layout's box.
/// </summary>
/// <remarks>
/// <para>
/// Lines are aligned within a box whose left edge is at X = 0. The box is
/// <see cref="TextLayoutOptions.MaxWidth"/> wide, or as wide as the widest line if
/// <see cref="TextLayoutOptions.MaxWidth"/> is infinite, in which case the widest line starts at
/// X = 0 and the other lines are aligned relative to it.
/// </para>
/// <para>
/// A line wider than the box overflows it on the side/sides that its alignment doesn't hold in
/// place: a line aligned to the left edge overflows past the right edge, a line aligned to the
/// right edge overflows past the left dege (into negative X), and a <see cref="Center"/> line
/// overflows equally past both.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TextHorizontalAlign {
	/// <summary>Raw switch tag for <see cref="TextHorizontalAlign"/>.</summary>
	public enum Case {
		/// <summary>
		/// Lines start at the left edge within an LTR paragraph, and at the right edge within an RTL
		/// paragraph. The direction of a paragraph comes from its first strong character, or LTR if it
		/// doesn't have any.
		/// </summary>
		Start = 1,

		/// <summary>
		/// Lines are centered.
		/// </summary>
		Center,

		/// <summary>
		/// Lines end at the right edge within an LTR paragraph, and at the left edge within an RTL
		/// paragraph. The direction of a paragraph comes from its first strong character, or LTR if it
		/// doesn't have any.
		/// </summary>
		End,

		/// <summary>
		/// Lines start at the left edge, regardless of text direction.
		/// </summary>
		Left,

		/// <summary>
		/// Lines end at the right edge, regardless of text direction.
		/// </summary>
		Right,
	}
}

/// <summary>
/// What happens to lines wider than the layout's box.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TextHorizontalOverflow {
	/// <summary>Raw switch tag for <see cref="TextHorizontalOverflow"/>.</summary>
	public enum Case {
		/// <summary>
		/// Lines overflow the box on the side or sides their alignment leaves open (see
		/// <see cref="TextHorizontalAlign"/>).
		/// </summary>
		Overflow = 1,

		/// <summary>
		/// Lines wider than <see cref="TextLayoutOptions.MaxWidth"/> are truncated (see
		/// <see cref="TextLine.IsTruncated"/>) to fit it; the end is cut off, and
		/// in the <see cref="TextLayoutOptions.TruncationMarker"/> is added. If even the marker alone is
		/// wider than the box, it's left out too. If <see cref="TextLayoutOptions.MaxWidth"/> is
		/// infinite, a horizontal overflow can't happen in the first place, so this option never gets
		/// used.
		/// </summary>
		/// <remarks>
		/// With <see cref="TextWrapMode.Greedy"/>, a line can only become wider than the box if there's
		/// a single cluster wider than the whole box; this leaves such lines empty.
		/// </remarks>
		Cut,
	}
}

/// <summary>
/// Marker appended to truncated lines (see <see cref="TextLine.IsTruncated"/>).
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TextTruncationMarker {
	/// <summary>Raw switch tag for <see cref="TextTruncationMarker"/>.</summary>
	public enum Case {
		/// <summary>
		/// Truncated lines end without any marker.
		/// </summary>
		None = 1,

		/// <summary>
		/// Truncated lines end in an ellipsis.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The ellipsis has the style of the line's last kept character (or its first character, if
		/// none is kept), and is U+2026 HORIZONTAL ELLIPSIS from the style's primary font, or
		/// <c>"..."</c> in said font if it lacks U+2026. If the primary font lacks <c>'.'</c> too, then
		/// U+2026 is tried going through the rest of the fallback chain, then <c>"..."</c>. If no font
		/// has either, the ellipsis is <c>"..."</c> drawn with the primary font's missing glyph.
		/// </para>
		/// <para>
		/// The ellipsis goes at the end of the line in the direction of its paragraph, i.e. on the right
		/// for LTR text and on the left for RTL text. Trailing whitespace is dropped before placement of
		/// the ellipsis. With a finite <see cref="TextLayoutOptions.MaxWidth"/>, more of the line's text
		/// is dropped to make room for it, unless the line is already wider than the box (with
		/// <see cref="TextHorizontalOverflow.Overflow"/>), in which case it overflows the box further.
		/// If the ellipsis just by itself is wider than the box, then with
		/// <see cref="TextHorizontalOverflow.Overflow"/>, all of the line's text is dropped and the
		/// ellipsis overflows the box, and with <see cref="TextHorizontalOverflow.Cut"/>, the
		/// ellipsis is left out and the line is cut to fit without it.
		/// </para>
		/// <para>
		/// A line that breaks at a soft hyphen keeps said hyphen (see <see cref="TextWrapMode.Greedy"/>)
		/// before the ellipsis, unless text is dropped to make room for the ellipsis.
		/// </para>
		/// </remarks>
		Ellipsis,
	}
}

/// <summary>
/// One line of a <see cref="TextLayout"/>, in drawable units, relative to the layout's top-left
/// corner.
/// </summary>
/// <param name="Width">Width of the line's glyphs.</param>
/// <param name="BaselineY">Y coordinate of the line's baseline.</param>
/// <param name="Ascent">Distance from the baseline up to the top of the line.</param>
/// <param name="Descent">Distance from the baseline down to the bottom of the line's glyphs.</param>
/// <param name="LineGap">Extra space below <paramref name="Descent"/> before the next line.</param>
/// <param name="Height">
/// Vertical space the line takes up, i.e. <c>Ascent + Descent + LineGap</c>.
/// </param>
/// <param name="OffsetX">
/// X coordinate of the line's left edge, from alignment (see <see cref="TextHorizontalAlign"/>);
/// negative for a line that overflows the layout's box to the left.
/// </param>
/// <param name="IsTruncated">
/// Whether the line was truncated, i.e. whether the end of its text was dropped to fit the box
/// (see <see cref="TextHorizontalOverflow.Cut"/> and <see cref="TextTruncationMarker.Ellipsis"/>),
/// or the lines after it were dropped (see <see cref="TextLayoutOptions.MaxLines"/> and
/// <see cref="TextLayoutOptions.MaxHeight"/>). A truncated line ends in the
/// <see cref="TextLayoutOptions.TruncationMarker"/>.
/// </param>
/// <remarks>
/// <para>
/// The vertical metrics are the largest of the line's fonts' metrics, each scaled by the size of
/// the text using it, or, for an empty line, those of the primary font and size at its position.
/// A truncated line keeps the metrics of all of its text, including the dropped part, and the
/// ellipsis's fonts don't count.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is an empty, untruncated line at the origin
/// with every metric at 0.
/// </para>
/// </remarks>
public readonly record struct TextLine(
	float Width,
	float BaselineY,
	float Ascent,
	float Descent,
	float LineGap,
	float Height,
	float OffsetX,
	bool IsTruncated
);

internal enum LineBreakKind : byte {
	None,
	Soft,
	Hard,
}

/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="LineBreakKind.None"/> at
/// position 0.
/// </remarks>
internal readonly record struct LineBreakOpportunity(
	int Position,
	LineBreakKind Kind
);

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct LogicalBidiRun(
	int Start,
	int Length,
	Direction Direction
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Direction == default;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct VisualBidiRun(
	int Start,
	int Length,
	Direction Direction
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Direction == default;
}

/// <summary>
/// A shaped cluster within a paragraph, in source order. Widths are in drawable units, and source
/// positions are relative to the paragraph.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is an empty cluster of run 0 at position 0.
/// </remarks>
internal readonly record struct ParagraphCluster(
	int Run,
	int GlyphStart,
	int GlyphCount,
	int SourceStart,
	int SourceLength,
	float Width,
	bool IsWhitespaceOnly,
	bool UnsafeToBreak,
	LineBreakKind BreakAfter
) {
	public int SourceLimit => SourceStart + SourceLength;
}

/// <summary>
/// A range of a paragraph's clusters forming one line.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is an empty line at cluster 0.
/// </remarks>
internal readonly record struct LineSpan(
	int Start,
	int End
);

internal sealed class ParagraphRun {
	public required int Chain { get; init; }
	public required int FontIndex { get; init; }
	public required float Size { get; init; }
	public required TextItem Item { get; init; }
	public required ShapedRun Shaped { get; init; }
}

/// <summary>
/// A maximal range of the text whose spans share everything shaping depends on (fonts, size, and
/// language), but don't necessarily share e.g. colors. Positions are absolute.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is an empty range of chain 0 at position 0,
/// at size 0.
/// </remarks>
internal readonly record struct ShapingSpan(
	int Start,
	int Limit,
	int Chain,
	float Size,
	string Language
);

/// <remarks>
/// <para>
/// Positions are in drawable units relative to the layout's top-left corner, at the glyph's origin on
/// the baseline.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is glyph 0 of the first chain's primary font at
/// the origin, at size 0, transparent.
/// </para>
/// </remarks>
internal readonly record struct LaidOutGlyph(
	int Chain,
	int FontIndex,
	uint GlyphId,
	float X,
	float Y,
	float Size,
	int SourceIndex,
	SrgbColor32 Color
);

internal sealed class LayoutResult {
	public required LaidOutGlyph[] Glyphs { get; init; }
	public required TextLine[] Lines { get; init; }
	public required float Width { get; init; }
	public required float Height { get; init; }
	public required bool IsTruncated { get; init; }
}

internal static class TextLayouter {
	private const string ellipsisString = "…";
	private const string ellipsisFallbackString = "...";
	private const string hyphenString = "\u2010"; // U+2010 HYPHEN; it looks identical to '-' in most fonts
	private const string hyphenFallbackString = "-";
	private const char softHyphen = '\u00AD';
	private static readonly SearchValues<char> paraSeparators = SearchValues.Create("\n\r\u0085\u2029");

	// line separators that end a line within a paragraph, i.e. LS/VT/FF
	private static bool isLineSeparator(char c) => c is '\u2028' or '\v' or '\f';

	/// <summary>
	/// A line's clusters from one run, in [<see cref="Start"/>, <see cref="End"/>) of the
	/// paragraph's clusters. <see cref="Reshaped"/> is set if the line cuts the run at a cluster
	/// that's unsafe to break at, and holds the line's part of the run shaped on its own.
	/// </summary>
	/// <remarks>
	/// The <see langword="default"/> value is valid and is an empty piece of run 0.
	/// </remarks>
	private readonly record struct LinePiece(
		int Run,
		int Start,
		int End,
		ParagraphRun? Reshaped
	);

	/// <summary>
	/// What added text depends on: the index of the rich text span it takes its style from, the
	/// direction it's shaped in, and the script of the text before it.
	/// </summary>
	/// <remarks>
	/// The <see langword="default"/> value is invalid.
	/// </remarks>
	private readonly record struct AddedTextKey(
		int Span,
		Direction Direction,
		Script Script
	) : IHasInvalidDefault {
		bool IHasInvalidDefault.IsDefault => Direction == default;
	}

	/// <summary>
	/// Additional text not in the source, shaped from a single font. Used for an ellipsis or a soft
	/// hyphen's drawn hyphen.
	/// </summary>
	private sealed class AddedText {
		public required ParagraphRun Run;
		public required Direction Direction;
		public required float Width;
		public required SrgbColor32 Color;
	}

	/// <summary>
	/// One paragraph's analysis, runs, clusters, and lines. Positions other than
	/// <see cref="Start"/> are relative to the paragraph.
	/// </summary>
	private sealed class Paragraph : IDisposable {
		public int Start; // absolute
		public string Text = "";
		public TextAnalysis.ParagraphAnalysis? Analysis; // null for an empty paragraph
		public bool IsLast;
		public int Next; // absolute start of the next paragraph, if not IsLast
		public readonly List<ParagraphRun> Runs = new();
		public readonly List<ParagraphCluster> Clusters = new();
		public readonly List<LineSpan> Spans = new();

		public void Dispose() {
			Analysis?.Dispose();
			Analysis = null;
		}
	}

	private sealed class Scratch {
		public required RichText Text;
		public required int[] SpanChains;
		public required ResolvedFontFallbackChain[] Chains;
		public required ITextItemizer Itemizer;
		public required FallbackResolver Resolver;
		public required ShapeCache ShapeCache;
		public required ShapingSpan[] Shaping;
		public required FontLineMetrics[][] Metrics; // by chain, then font
		public required TextLayoutOptions Options;
		public required bool Wrap;
		public readonly List<LaidOutGlyph> Glyphs = new();
		public readonly List<TextLine> Lines = new();
		// index of each line's first glyph in Glyphs
		public readonly List<int> LineGlyphStarts = new();
		// each line's paragraph's direction, for Start/End alignment
		public readonly List<Direction> LineDirections = new();
		public readonly List<LinePiece> Pieces = new();
		// the current line's clusters, with reshaped runs' clusters in place of the originals
		public readonly List<ParagraphCluster> LineClusters = new();
		public readonly List<LogicalBidiRun> LogicalRuns = new();
		public readonly List<VisualBidiRun> VisualRuns = new();
		public readonly List<ResolvedItem> Resolved = new();
		// each paragraph's breaks, by language
		public readonly Dictionary<string, LineBreakOpportunity[]> ParagraphBreaks = new(StringComparer.Ordinal);
		public readonly Dictionary<AddedTextKey, AddedText> Ellipses = new();
		public readonly Dictionary<AddedTextKey, AddedText> Hyphens = new();
	}

	/// <summary>
	/// Lays out rich text.
	/// </summary>
	/// <remarks>
	/// <paramref name="spanChains"/> holds the index into <paramref name="chains"/> of each span's
	/// fonts.
	/// </remarks>
	public static LayoutResult Layout(
		ITextItemizer itemizer,
		FallbackResolver resolver,
		ShapeCache shapeCache,
		ResolvedFontFallbackChain[] chains,
		RichText text,
		int[] spanChains,
		in TextLayoutOptions options
	) {
		var metrics = new FontLineMetrics[chains.Length][];
		for (int c = 0; c < chains.Length; c++) {
			metrics[c] = new FontLineMetrics[chains[c].Fonts.Length];
			for (int f = 0; f < metrics[c].Length; f++)
				metrics[c][f] = chains[c].States[f].LineMetrics;
		}
		Scratch s = new() {
			Text = text,
			SpanChains = spanChains,
			Chains = chains,
			Itemizer = itemizer,
			Resolver = resolver,
			ShapeCache = shapeCache,
			Shaping = ShapingSpans(text, spanChains),
			Metrics = metrics,
			Options = options,
			Wrap = options.WrapMode == TextWrapMode.Greedy && !float.IsPositiveInfinity(options.MaxWidth),
		};
		int maxLines = options.MaxLines ?? int.MaxValue;
		float top = 0f;
		float width = 0f;
		bool truncated = false;
		// the paragraph after the current one is prepared early if MaxHeight needs its first line
		using Paragraph para0 = new(), para1 = new();
		Paragraph cur = para0, next = para1;
		bool nextReady = false;
		prepare(s, cur, 0);
		for (bool done = false; !done;) {
			for (int j = 0; j < cur.Spans.Count; j++) {
				FontLineMetrics m = lineMetrics(s, cur, cur.Spans[j]);
				bool dropAfter = false;
				if (j + 1 < cur.Spans.Count || !cur.IsLast) {
					if (s.Lines.Count + 1 >= maxLines) {
						dropAfter = true;
					} else if (!float.IsPositiveInfinity(options.MaxHeight)) {
						FontLineMetrics nextM;
						if (j + 1 < cur.Spans.Count) {
							nextM = lineMetrics(s, cur, cur.Spans[j + 1]);
						} else {
							if (!nextReady)
								prepare(s, next, cur.Next);
							nextReady = true;
							nextM = lineMetrics(s, next, next.Spans[0]);
						}
						dropAfter = top + m.Height + nextM.Ascent + nextM.Descent > options.MaxHeight;
					}
				}
				truncated |= emitLine(s, cur, cur.Spans[j], m, dropAfter, ref top, ref width);
				if (dropAfter) {
					done = true;
					break;
				}
			}
			if (done || cur.IsLast)
				break;
			if (!nextReady)
				prepare(s, next, cur.Next);
			nextReady = false;
			(cur, next) = (next, cur);
		}
		align(s, float.IsPositiveInfinity(options.MaxWidth) ? width : options.MaxWidth, options.HorizontalAlign);
		// there's always a line, and the last one's line gap isn't part of the text
		TextLine last = s.Lines[^1];
		return new LayoutResult {
			Glyphs = s.Glyphs.ToArray(),
			Lines = s.Lines.ToArray(),
			Width = width,
			Height = last.BaselineY + last.Descent,
			IsTruncated = truncated,
		};
	}

	/// <summary>
	/// Merges adjacent spans that differ only in color, since color doesn't affect shaping.
	/// </summary>
	internal static ShapingSpan[] ShapingSpans(RichText text, int[] spanChains) {
		ReadOnlySpan<RichTextSpan> spans = text.SpansSpan;
		List<ShapingSpan> result = new(spans.Length);
		for (int i = 0; i < spans.Length; i++) {
			RichTextSpan span = spans[i];
			ShapingSpan next = new(span.Start, span.Limit, spanChains[i], span.Style.Size, span.Style.Language);
			if (result.Count > 0) {
				ShapingSpan prev = result[^1];
				if (prev.Chain == next.Chain && prev.Size == next.Size && prev.Language == next.Language) {
					result[^1] = prev with { Limit = next.Limit };
					continue;
				}
			}
			result.Add(next);
		}
		return result.ToArray();
	}

	// the shaping span containing the code unit at `index`; the last one for an index at the end
	private static int shapingSpanAt(ReadOnlySpan<ShapingSpan> spans, int index) {
		int lo = 0, hi = spans.Length - 1;
		while (lo < hi) {
			int mid = lo + (hi - lo + 1) / 2;
			if (spans[mid].Start <= index)
				lo = mid;
			else
				hi = mid - 1;
		}
		return lo;
	}

	// analyzes, shapes, and wraps the paragraph starting at `start`
	private static void prepare(Scratch s, Paragraph p, int start) {
		p.Dispose();
		p.Runs.Clear();
		p.Clusters.Clear();
		p.Spans.Clear();
		string str = s.Text.Text;
		int sep = str.AsSpan(start).IndexOfAny(paraSeparators);
		int limit = sep < 0 ? str.Length : start + sep;
		p.Start = start;
		p.Text = str[start..limit];
		p.IsLast = sep < 0;
		// crlf is the only separator wider than one char
		p.Next = limit + (str.AsSpan(limit).StartsWith("\r\n") ? 2 : 1);
		if (p.Text.Length == 0) {
			p.Spans.Add(default);
			return;
		}
		p.Analysis = TextAnalysis.ParagraphAnalysis.Create(p.Text);
		buildRuns(s, p, p.Analysis);
		buildClusters(s, p);
		Wrap(
			CollectionsMarshal.AsSpan(p.Clusters),
			s.Options.MaxWidth,
			s.Wrap,
			(lineStart, lineEnd) => measureLine(s, p, lineStart, lineEnd) + hyphenWidth(s, p, lineEnd),
			p.Spans
		);
	}

	private static void buildRuns(Scratch s, Paragraph p, TextAnalysis.ParagraphAnalysis analysis) {
		ReadOnlySpan<ShapingSpan> shaping = s.Shaping;
		s.LogicalRuns.Clear();
		analysis.GetLogicalRuns(s.LogicalRuns);
		foreach (LogicalBidiRun bidiRun in s.LogicalRuns) {
			int runStart = p.Start + bidiRun.Start;
			int runLimit = runStart + bidiRun.Length;
			// each bidi run is cut further where the fonts, size, or language change
			for (int k = shapingSpanAt(shaping, runStart); k < shaping.Length && shaping[k].Start < runLimit; k++) {
				ShapingSpan span = shaping[k];
				int segStart = Math.Max(runStart, span.Start) - p.Start;
				int segLimit = Math.Min(runLimit, span.Limit) - p.Start;
				if (segLimit <= segStart)
					continue;
				TextItem[] items = s.Itemizer.Itemize(p.Text.AsSpan(segStart, segLimit - segStart), segStart, bidiRun.Direction, span.Language);
				foreach (TextItem item in items) {
					s.Resolved.Clear();
					s.Resolver.ResolveItems(s.Chains[span.Chain], item, s.Resolved);
					foreach (ResolvedItem r in s.Resolved) {
						p.Runs.Add(new ParagraphRun {
							Chain = span.Chain,
							FontIndex = r.FontIndex,
							Size = span.Size,
							Item = r.Item,
							Shaped = r.ShapedRun,
						});
					}
				}
			}
		}
	}

	private static void buildClusters(Scratch s, Paragraph p) {
		string para = p.Text;
		Span<LineBreakKind> breakAfter = (para.Length + 1) <= 2048
			? stackalloc LineBreakKind[para.Length + 1]
			: new LineBreakKind[para.Length + 1];
		breakAfter.Clear();
		// a break after a character follows that character's language; line breaking takes a single
		// language, so the paragraph is analyzed once per distinct language in it, with full context
		ReadOnlySpan<ShapingSpan> shaping = s.Shaping;
		s.ParagraphBreaks.Clear();
		int paraLimit = p.Start + para.Length;
		for (int k = shapingSpanAt(shaping, p.Start); k < shaping.Length && shaping[k].Start < paraLimit; k++) {
			ShapingSpan span = shaping[k];
			string key = span.Language;
			if (!s.ParagraphBreaks.TryGetValue(key, out LineBreakOpportunity[]? breaks)) {
				breaks = TextAnalysis.GetLineBreaks(para, key);
				s.ParagraphBreaks.Add(key, breaks);
			}
			// breaks after the span's characters, i.e. at positions (start, limit] relative to the paragraph
			int lo = Math.Max(span.Start, p.Start) - p.Start;
			int hi = Math.Min(span.Limit, paraLimit) - p.Start;
			foreach (LineBreakOpportunity opp in breaks)
				if (opp.Position > lo && opp.Position <= hi)
					breakAfter[opp.Position] = opp.Kind;
		}
		for (int r = 0; r < p.Runs.Count; r++)
			appendClusters(p.Clusters, r, p.Runs[r], breakAfter);
	}

	// line separators are shaped like any other character but mustn't be drawn
	private static bool isHidden(ParagraphRun run, in ShapedCluster c) =>
		c.SourceLength == 1 && isLineSeparator(run.Item.Text[c.SourceStart]);

	/// <summary>
	/// Appends the clusters of run <paramref name="runIndex"/> to <paramref name="dst"/>, reading the
	/// breaks from <paramref name="breakAfter"/> indexed by paragraph-relative positions, or using
	/// <see cref="LineBreakKind.None"/> if it's empty.
	/// </summary>
	private static void appendClusters(List<ParagraphCluster> dst, int runIndex, ParagraphRun run, ReadOnlySpan<LineBreakKind> breakAfter) {
		foreach (ShapedCluster c in run.Shaped.SourceOrderClusters) {
			int start = run.Item.SourceStart + c.SourceStart;
			bool hidden = isHidden(run, c);
			dst.Add(new ParagraphCluster(
				Run: runIndex,
				GlyphStart: c.GlyphStart,
				GlyphCount: hidden ? 0 : c.GlyphCount,
				SourceStart: start,
				SourceLength: c.SourceLength,
				Width: hidden ? 0f : c.Width * run.Size,
				IsWhitespaceOnly: c.IsWhitespaceOnly,
				UnsafeToBreak: c.UnsafeToBreak,
				BreakAfter: breakAfter.IsEmpty ? LineBreakKind.None : breakAfter[start + c.SourceLength]
			));
		}
	}

	/// <summary>
	/// Breaks a paragraph's clusters into lines; whitespace never causes a wrap by itself, and is
	/// trimmed from both sides of a wrap. A hard break at the end of the paragraph is followed by an
	/// empty line.
	/// </summary>
	/// <remarks>
	/// <paramref name="measure"/> measures the width of a line made of the clusters in [start, end),
	/// which is not the same as the sum of their widths if the line's ends are shaped again. Lines
	/// are filled by the clusters' widths, then each line that may need breaking is checked via
	/// <paramref name="measure"/>; a line that doesn't fit breaks at an earlier opportunity, or
	/// within its first word if there still isn't one that fits. Since filling uses the widths in the
	/// unbroken text, a line whose ends get narrower when shaped again may break earlier than it has
	/// to.
	/// </remarks>
	public static void Wrap(
		ReadOnlySpan<ParagraphCluster> clusters,
		float maxWidth,
		bool wrap,
		Func<int, int, float> measure,
		List<LineSpan> dst
	) {
		if (clusters.IsEmpty) {
			dst.Add(default);
			return;
		}
		int start = 0;
		float width = 0f;
		int i = 0;
		for (;;) {
			if (i == clusters.Length) {
				// text after a line separator starts a new line, even if it's empty
				if (start == clusters.Length) {
					if (clusters[^1].BreakAfter == LineBreakKind.Hard)
						dst.Add(new LineSpan(start, start));
					break;
				}
				if (wrap && !fits(clusters, start, i, maxWidth, measure)) {
					start = addWrappedLine(clusters, start, findBreak(clusters, start, i - 1, maxWidth, measure), dst);
					i = start;
					width = 0f;
					continue;
				}
				dst.Add(new LineSpan(start, i));
				break;
			}
			ParagraphCluster c = clusters[i];
			if (wrap && i > start && !c.IsWhitespaceOnly && width + c.Width > maxWidth) {
				start = addWrappedLine(clusters, start, findBreak(clusters, start, i, maxWidth, measure), dst);
				i = start;
				width = 0f;
				continue;
			}
			width += c.Width;
			if (c.BreakAfter == LineBreakKind.Hard) {
				if (wrap && !fits(clusters, start, i + 1, maxWidth, measure)) {
					start = addWrappedLine(clusters, start, findBreak(clusters, start, i, maxWidth, measure), dst);
					i = start;
					width = 0f;
					continue;
				}
				dst.Add(new LineSpan(start, i + 1));
				start = i + 1;
				width = 0f;
			}
			i++;
		}
	}

	// whether the line [start, end) fits, without trailing whitespace
	// a single cluster always does, since it can't be broken further
	private static bool fits(ReadOnlySpan<ParagraphCluster> clusters, int start, int end, float maxWidth, Func<int, int, float> measure) {
		int trimmed = TrimTrailingWs(clusters, start, end);
		return trimmed - start <= 1 || measure(start, trimmed) <= maxWidth;
	}

	// adds the line [start, brk) wrapped at brk, and returns where the next one starts
	private static int addWrappedLine(ReadOnlySpan<ParagraphCluster> clusters, int start, int brk, List<LineSpan> dst) {
		dst.Add(new LineSpan(start, TrimTrailingWs(clusters, start, brk)));
		return SkipLeadingWs(clusters, brk);
	}

	/// <summary>
	/// Gets the last position in (<paramref name="start"/>, <paramref name="last"/>] to wrap a line
	/// starting at <paramref name="start"/> at: the last soft break opportunity at which the line
	/// fits, or if there's none, the last position within the line's first word at which it fits, or
	/// if there's none, right after the first cluster.
	/// </summary>
	private static int findBreak(ReadOnlySpan<ParagraphCluster> clusters, int start, int last, float maxWidth, Func<int, int, float> measure) {
		int firstSoft = -1;
		for (int p = last; p > start; p--) {
			if (clusters[p - 1].BreakAfter != LineBreakKind.Soft)
				continue;
			firstSoft = p;
			int trimmed = TrimTrailingWs(clusters, start, p);
			if (trimmed > start && measure(start, trimmed) <= maxWidth)
				return p;
		}
		// the first word doesn't fit by itself, so it's broken within
		for (int p = firstSoft >= 0 ? firstSoft - 1 : last; p > start + 1; p--)
			if (measure(start, TrimTrailingWs(clusters, start, p)) <= maxWidth)
				return p;
		return start + 1;
	}

	public static int SkipLeadingWs(ReadOnlySpan<ParagraphCluster> clusters, int start) {
		while (start < clusters.Length && clusters[start].IsWhitespaceOnly)
			start++;
		return start;
	}

	public static int TrimTrailingWs(ReadOnlySpan<ParagraphCluster> clusters, int start, int end) {
		while (end > start && clusters[end - 1].IsWhitespaceOnly)
			end--;
		return end;
	}

	/// <summary>
	/// Gets the first index in [<paramref name="lo"/>, <paramref name="hi"/>) whose cluster starts at
	/// or after <paramref name="position"/>, or <paramref name="hi"/> if none does.
	/// </summary>
	private static int lowerBound(ReadOnlySpan<ParagraphCluster> clusters, int lo, int hi, int position) {
		while (lo < hi) {
			int mid = lo + (hi - lo) / 2;
			if (clusters[mid].SourceStart < position)
				lo = mid + 1;
			else
				hi = mid;
		}
		return lo;
	}

	/// <summary>
	/// Splits the line [<paramref name="start"/>, <paramref name="end"/>) of the paragraph's
	/// clusters into pieces by run, shaping a piece again if the line cuts its run at a cluster
	/// that's unsafe to break at.
	/// </summary>
	private static void linePieces(Scratch s, Paragraph p, int start, int end, List<LinePiece> dst) {
		ReadOnlySpan<ParagraphCluster> clusters = CollectionsMarshal.AsSpan(p.Clusters);
		dst.Clear();
		int a = start;
		while (a < end) {
			int r = clusters[a].Run;
			int b = a + 1;
			while (b < end && clusters[b].Run == r)
				b++;
			// we don't need to do anything for a cut between runs, since they're shaped separately anyways
			bool cutBefore = a > 0 && clusters[a - 1].Run == r && clusters[a].UnsafeToBreak;
			bool cutAfter = b < clusters.Length && clusters[b].Run == r && clusters[b].UnsafeToBreak;
			ParagraphRun? reshaped = cutBefore || cutAfter
				? reshape(s, p.Runs[r], clusters[a].SourceStart, clusters[b - 1].SourceLimit)
				: null;
			dst.Add(new LinePiece(r, a, b, reshaped));
			a = b;
		}
	}

	// independently reshapes the part of `run` in [sourceStart, sourceLimit) (relative to the
	// paragraph)
	private static ParagraphRun reshape(Scratch s, ParagraphRun run, int sourceStart, int sourceLimit) {
		TextItem item = new(
			SourceStart: sourceStart,
			Text: run.Item.Text.Substring(sourceStart - run.Item.SourceStart, sourceLimit - sourceStart),
			Properties: run.Shaped.Properties
		);
		return new ParagraphRun {
			Chain = run.Chain,
			FontIndex = run.FontIndex,
			Size = run.Size,
			Item = item,
			Shaped = s.ShapeCache.GetOrCreate(s.Chains[run.Chain].Fonts[run.FontIndex], item),
		};
	}

	private static float measureLine(Scratch s, Paragraph p, int start, int end) {
		ReadOnlySpan<ParagraphCluster> clusters = CollectionsMarshal.AsSpan(p.Clusters);
		linePieces(s, p, start, end, s.Pieces);
		float width = 0f;
		foreach (LinePiece piece in s.Pieces) {
			if (piece.Reshaped is not null) {
				foreach (ShapedCluster c in piece.Reshaped.Shaped.SourceOrderClusters)
					if (!isHidden(piece.Reshaped, c))
						width += c.Width * piece.Reshaped.Size;
			} else {
				for (int k = piece.Start; k < piece.End; k++)
					width += clusters[k].Width;
			}
		}
		return width;
	}

	// fills s.LineClusters with a line's clusters, adding the runs of reshaped pieces to p.Runs
	private static void buildLineClusters(Scratch s, Paragraph p, int start, int end) {
		s.LineClusters.Clear();
		linePieces(s, p, start, end, s.Pieces);
		foreach (LinePiece piece in s.Pieces) {
			if (piece.Reshaped is not null) {
				p.Runs.Add(piece.Reshaped);
				appendClusters(s.LineClusters, p.Runs.Count - 1, piece.Reshaped, default);
			} else {
				s.LineClusters.AddRange(CollectionsMarshal.AsSpan(p.Clusters)[piece.Start..piece.End]);
			}
		}
	}

	// the position (relative to the paragraph) of cluster `index`, or the paragraph's end for the
	// index after the last cluster
	private static int positionOf(Paragraph p, int index) =>
		index < p.Clusters.Count ? p.Clusters[index].SourceStart : p.Text.Length;

	// the base direction of the paragraph's text at cluster `index` (see positionOf)
	private static Direction directionAt(Paragraph p, int index) =>
		p.Analysis?.BaseDirectionAt(Math.Min(positionOf(p, index), p.Text.Length - 1)) ?? Direction.LeftToRight;

	/// <summary>
	/// Gets a line's vertical metrics: the largest of its runs' fonts' metrics scaled by the runs'
	/// sizes, or, for an empty line, those of the primary font and size at its position.
	/// </summary>
	private static FontLineMetrics lineMetrics(Scratch s, Paragraph p, LineSpan span) {
		if (span.End == span.Start) {
			ShapingSpan at = s.Shaping[shapingSpanAt(s.Shaping, p.Start + positionOf(p, span.Start))];
			FontLineMetrics m = s.Metrics[at.Chain][0];
			return new FontLineMetrics(m.Ascent * at.Size, m.Descent * at.Size, m.LineGap * at.Size);
		}
		float ascent = 0f, descent = 0f, lineGap = 0f;
		foreach (ParagraphCluster c in CollectionsMarshal.AsSpan(p.Clusters)[span.Start..span.End]) {
			ParagraphRun run = p.Runs[c.Run];
			FontLineMetrics m = s.Metrics[run.Chain][run.FontIndex];
			ascent = MathF.Max(ascent, m.Ascent * run.Size);
			descent = MathF.Max(descent, m.Descent * run.Size);
			lineGap = MathF.Max(lineGap, m.LineGap * run.Size);
		}
		return new FontLineMetrics(ascent, descent, lineGap);
	}

	/// <summary>
	/// Lays out one line with the vertical metrics <paramref name="m"/>, truncating it if it's wider
	/// than the box with <see cref="TextHorizontalOverflow.Cut"/> or if
	/// <paramref name="dropAfter"/>, i.e. the lines after it get dropped. Returns whether it was
	/// truncated.
	/// </summary>
	private static bool emitLine(
		Scratch s,
		Paragraph p,
		LineSpan span,
		in FontLineMetrics m,
		bool dropAfter,
		ref float top,
		ref float width
	) {
		ReadOnlySpan<ParagraphCluster> clusters = CollectionsMarshal.AsSpan(p.Clusters);
		int full = TrimTrailingWs(clusters, span.Start, span.End);
		// a soft hyphen isn't whitespace, so a line breaking at one has full == span.End
		AddedText? hyphen = breaksAtSoftHyphen(p, full) ? hyphenAt(s, p, full) : null;
		float hyphenW = hyphen?.Width ?? 0f;
		bool cut = s.Options.HorizontalOverflow == TextHorizontalOverflow.Cut
			&& full > span.Start
			&& measureLine(s, p, span.Start, full) + hyphenW > s.Options.MaxWidth;
		bool truncated = cut || dropAfter;
		int keptEnd = span.End;
		AddedText? ellipsis = null;
		if (truncated)
			(keptEnd, ellipsis) = truncate(s, p, span, full, cut, hyphenW);
		// a line keeps the hyphen only if truncation didn't cut it before the soft hyphen
		if (keptEnd != full)
			hyphen = null;
		buildLineClusters(s, p, span.Start, keptEnd);
		ReadOnlySpan<ParagraphCluster> line = CollectionsMarshal.AsSpan(s.LineClusters);
		float baselineY = top + m.Ascent;
		s.LineGlyphStarts.Add(s.Glyphs.Count);
		s.LineDirections.Add(directionAt(p, span.Start));
		float penX = 0f;
		int ellipsisSource = ellipsis is not null ? firstDropped(p, span.Start, keptEnd) : 0;
		if (ellipsis is not null && ellipsis.Direction == Direction.RightToLeft)
			emitAddedText(s, ellipsis, ellipsisSource, baselineY, ref penX);
		if (!line.IsEmpty && p.Analysis is not null) {
			s.VisualRuns.Clear();
			p.Analysis.GetVisualRunsForLine(s.VisualRuns, line[0].SourceStart, line[^1].SourceLimit);
			// the hyphen goes right after the soft hyphen, which ends the line logically, in the
			// direction of its run
			int hyphenSource = hyphen is not null ? p.Start + line[^1].SourceLimit - 1 : 0;
			foreach (VisualBidiRun v in s.VisualRuns) {
				int a = lowerBound(line, 0, line.Length, v.Start);
				int b = lowerBound(line, a, line.Length, v.Start + v.Length);
				if (v.Direction == Direction.RightToLeft) {
					for (int k = b - 1; k >= a; k--) {
						if (hyphen is not null && k == line.Length - 1)
							emitAddedText(s, hyphen, hyphenSource, baselineY, ref penX);
						emitCluster(s, p, line[k], baselineY, ref penX);
					}
				} else {
					for (int k = a; k < b; k++) {
						emitCluster(s, p, line[k], baselineY, ref penX);
						if (hyphen is not null && k == line.Length - 1)
							emitAddedText(s, hyphen, hyphenSource, baselineY, ref penX);
					}
				}
			}
		}
		if (ellipsis is not null && ellipsis.Direction == Direction.LeftToRight)
			emitAddedText(s, ellipsis, ellipsisSource, baselineY, ref penX);
		s.Lines.Add(new TextLine(
			Width: penX,
			BaselineY: baselineY,
			Ascent: m.Ascent,
			Descent: m.Descent,
			LineGap: m.LineGap,
			Height: m.Height,
			OffsetX: 0f, // set by align()
			IsTruncated: truncated
		));
		top += m.Height;
		width = MathF.Max(width, penX);
		return truncated;
	}

	/// <summary>
	/// Gets where a truncated line's kept clusters end, and its ellipsis, if any.
	/// <paramref name="full"/> is the end of the line without trailing whitespace,
	/// <paramref name="cut"/> is whether it's wider than the box with
	/// <see cref="TextHorizontalOverflow.Cut"/>, and <paramref name="hyphenWidth"/> is the width of
	/// the hyphen it ends in if it's kept up to <paramref name="full"/>.
	/// </summary>
	private static (int KeptEnd, AddedText? Ellipsis) truncate(Scratch s, Paragraph p, LineSpan span, int full, bool cut, float hyphenWidth) {
		bool marker = s.Options.TruncationMarker == TextTruncationMarker.Ellipsis;
		// with no marker and nothing to cut, the line stays as it is
		if (!marker && !cut)
			return (span.End, null);
		// with an infinite width, or a line already wider than the box with Overflow, the ellipsis
		// goes after all of the text
		if (float.IsPositiveInfinity(s.Options.MaxWidth) || !cut && measureLine(s, p, span.Start, full) + hyphenWidth > s.Options.MaxWidth)
			return (full, ellipsisAt(s, p, span.Start, full));
		if (fitEnd(s, p, span.Start, full, hyphenWidth, marker, out int keptEnd, out AddedText? ellipsis))
			return (keptEnd, ellipsis);
		// only an ellipsis wider than the box doesn't fit even without any text, which Cut drops
		if (s.Options.HorizontalOverflow == TextHorizontalOverflow.Overflow)
			return (span.Start, ellipsisAt(s, p, span.Start, span.Start));
		fitEnd(s, p, span.Start, full, hyphenWidth, false, out keptEnd, out _);
		return (keptEnd, null);
	}

	/// <summary>
	/// Gets the last end in [<paramref name="start"/>, <paramref name="full"/>] for a line's kept
	/// clusters at which the line, without trailing whitespace, plus an added ellipsis if
	/// <paramref name="withEllipsis"/>, fits the box. Ending at <paramref name="full"/> also keeps
	/// a hyphen <paramref name="hyphenWidth"/> wide. Returns <see langword="false"/> if none does,
	/// which can only happen if there's an ellipsis and said ellipsis just by itself is wider than
	/// the box.
	/// </summary>
	private static bool fitEnd(
		Scratch s,
		Paragraph p,
		int start,
		int full,
		float hyphenWidth,
		bool withEllipsis,
		out int keptEnd,
		out AddedText? ellipsis
	) {
		ReadOnlySpan<ParagraphCluster> clusters = CollectionsMarshal.AsSpan(p.Clusters);
		float maxWidth = s.Options.MaxWidth;
		// the sum of the clusters' widths only skips ends that can't fit, since measuring isn't free
		float sum = 0f;
		for (int k = start; k < full; k++)
			sum += clusters[k].Width;
		for (int e = full; e >= start; e--) {
			if (e < full)
				sum -= clusters[e].Width;
			// ends after whitespace are the same as the end before it
			if (e > start && clusters[e - 1].IsWhitespaceOnly)
				continue;
			AddedText? el = withEllipsis ? ellipsisAt(s, p, start, e) : null;
			float extra = (el?.Width ?? 0f) + (e == full ? hyphenWidth : 0f);
			if (sum + extra > maxWidth)
				continue;
			if (e == start || measureLine(s, p, start, e) + extra <= maxWidth) {
				keptEnd = e;
				ellipsis = el;
				return true;
			}
		}
		keptEnd = start;
		ellipsis = null;
		return false;
	}

	// the absolute position of the first character a line truncated at `keptEnd` leaves out
	private static int firstDropped(Paragraph p, int start, int keptEnd) {
		ReadOnlySpan<ParagraphCluster> clusters = CollectionsMarshal.AsSpan(p.Clusters);
		return p.Start + (keptEnd > start ? clusters[keptEnd - 1].SourceLimit : positionOf(p, start));
	}

	/// <summary>
	/// Gets the ellipsis for a line starting at cluster <paramref name="start"/> whose kept clusters
	/// end at <paramref name="keptEnd"/>. The ellipsis gets the style of the last kept cluster's
	/// first character, or of the line's first character if none is kept.
	/// </summary>
	private static AddedText ellipsisAt(Scratch s, Paragraph p, int start, int keptEnd) {
		ReadOnlySpan<ParagraphCluster> clusters = CollectionsMarshal.AsSpan(p.Clusters);
		int styleIndex = p.Start + positionOf(p, start);
		Script script = Script.Common;
		if (start < clusters.Length) {
			ParagraphCluster c = clusters[keptEnd > start ? keptEnd - 1 : start];
			styleIndex = p.Start + c.SourceStart;
			script = p.Runs[c.Run].Shaped.Properties.Script ?? Script.Common;
		}
		// the style at the end of the text, for an empty line there
		styleIndex = Math.Min(styleIndex, s.Text.Text.Length - 1);
		AddedTextKey key = new(s.Text.SpanIndexAt(styleIndex), directionAt(p, start), script);
		if (!s.Ellipses.TryGetValue(key, out AddedText? ellipsis)) {
			ellipsis = makeAddedText(s, key, ellipsisString, ellipsisFallbackString, true);
			s.Ellipses.Add(key, ellipsis);
		}
		return ellipsis;
	}

	/// <summary>
	/// Shapes added text, trying, in order:
	/// <list type="bullet">
	/// <item><description>
	/// <paramref name="normal"/> from the primary font;
	/// </description></item>
	/// <item><description>
	/// <paramref name="fallback"/> from the primary font;
	/// </description></item>
	/// <item><description>
	/// if <paramref name="normalFromFallbacks"/> is true, <paramref name="normal"/> from the first
	/// fallback font that has it;
	/// </description></item>
	/// <item><description>
	/// <paramref name="fallback"/> from the first fallback font that has it.
	/// </description></item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// If no font has either of these, the result is <paramref name="fallback"/> from the primary
	/// font as .notdefs.
	/// </remarks>
	private static AddedText makeAddedText(Scratch s, AddedTextKey key, string normal, string fallback, bool normalFromFallbacks) {
		TextStyle style = s.Text.SpansSpan[key.Span].Style;
		int chain = s.SpanChains[key.Span];
		ResolvedFontFallbackChain fonts = s.Chains[chain];
		TextSegmentProperties props = new(key.Direction, key.Script, style.Language);
		TextItem normalItem = new(0, normal, props);
		TextItem fallbackItem = new(0, fallback, props);
		ParagraphRun? run = tryFont(0, normalItem) ?? tryFont(0, fallbackItem);
		for (int f = 1; normalFromFallbacks && run is null && f < fonts.Fonts.Length; f++)
			run = tryFont(f, normalItem);
		for (int f = 1; run is null && f < fonts.Fonts.Length; f++)
			run = tryFont(f, fallbackItem);
		run ??= newRun(0, fallbackItem, s.ShapeCache.GetOrCreate(fonts.Primary, fallbackItem));
		return new AddedText {
			Run = run,
			Direction = key.Direction,
			Width = run.Shaped.Width * style.Size,
			Color = style.Color,
		};

		ParagraphRun? tryFont(int font, TextItem item) {
			ShapedRun shaped = s.ShapeCache.GetOrCreate(fonts.Fonts[font], item);
			return FallbackResolver.NoNotdefs(shaped) ? newRun(font, item, shaped) : null;
		}

		ParagraphRun newRun(int font, TextItem item, ShapedRun shaped) => new() {
			Chain = chain,
			FontIndex = font,
			Size = style.Size,
			Item = item,
			Shaped = shaped,
		};
	}

	// whether a line ending at cluster `end` breaks at a soft hyphen, and so ends in a hyphen
	private static bool breaksAtSoftHyphen(Paragraph p, int end) {
		ReadOnlySpan<ParagraphCluster> clusters = CollectionsMarshal.AsSpan(p.Clusters);
		return end > 0 && end < clusters.Length && p.Text[clusters[end - 1].SourceLimit - 1] == softHyphen;
	}

	// the hyphen of a line ending at cluster `end`, right after a soft hyphen, in its style
	private static AddedText hyphenAt(Scratch s, Paragraph p, int end) {
		ParagraphCluster c = p.Clusters[end - 1];
		ShapedRun shaped = p.Runs[c.Run].Shaped;
		AddedTextKey key = new(
			s.Text.SpanIndexAt(p.Start + c.SourceLimit - 1),
			shaped.Properties.Direction ?? Direction.LeftToRight,
			shaped.Properties.Script ?? Script.Common
		);
		if (!s.Hyphens.TryGetValue(key, out AddedText? hyphen)) {
			hyphen = makeAddedText(s, key, hyphenString, hyphenFallbackString, false);
			s.Hyphens.Add(key, hyphen);
		}
		return hyphen;
	}

	// the width of the hyphen a line ending at cluster `end` ends in, or 0 if it doesn't
	private static float hyphenWidth(Scratch s, Paragraph p, int end) =>
		breaksAtSoftHyphen(p, end) ? hyphenAt(s, p, end).Width : 0f;

	// each glyph takes the color of its cluster's first character
	private static void emitCluster(Scratch s, Paragraph p, in ParagraphCluster c, float baselineY, ref float penX) {
		ParagraphRun run = p.Runs[c.Run];
		ReadOnlySpan<RichTextSpan> spans = s.Text.SpansSpan;
		ReadOnlySpan<ShapedGlyph> glyphs = run.Shaped.Glyphs.AsSpan(c.GlyphStart, c.GlyphCount);
		float size = run.Size;
		foreach (ShapedGlyph g in glyphs) {
			int source = p.Start + run.Item.SourceStart + (int)g.Cluster;
			s.Glyphs.Add(new LaidOutGlyph(
				Chain: run.Chain,
				FontIndex: run.FontIndex,
				GlyphId: g.GlyphId,
				X: penX + g.XOffset * size,
				Y: baselineY - g.YOffset * size,
				Size: size,
				SourceIndex: source,
				Color: spans[s.Text.SpanIndexAt(source)].Style.Color
			));
			penX += g.XAdvance * size;
			// TODO: YAdvance, for vertical text
		}
	}

	// every glyph of added text has `source` as its source index
	private static void emitAddedText(Scratch s, AddedText added, int source, float baselineY, ref float penX) {
		ParagraphRun run = added.Run;
		float size = run.Size;
		foreach (ShapedGlyph g in run.Shaped.Glyphs) {
			s.Glyphs.Add(new LaidOutGlyph(
				Chain: run.Chain,
				FontIndex: run.FontIndex,
				GlyphId: g.GlyphId,
				X: penX + g.XOffset * size,
				Y: baselineY - g.YOffset * size,
				Size: size,
				SourceIndex: source,
				Color: added.Color
			));
			penX += g.XAdvance * size;
		}
	}

	/// <summary>
	/// Offsets each line horizontally to align it within a box of width <paramref name="boxWidth"/>
	/// starting at x = 0.
	/// </summary>
	private static void align(Scratch s, float boxWidth, TextHorizontalAlign align) {
		Span<TextLine> lines = CollectionsMarshal.AsSpan(s.Lines);
		Span<LaidOutGlyph> glyphs = CollectionsMarshal.AsSpan(s.Glyphs);
		for (int i = 0; i < lines.Length; i++) {
			float offsetX = alignOffset(lines[i].Width, boxWidth, align, s.LineDirections[i]);
			if (offsetX == 0f)
				continue;
			int end = i + 1 < lines.Length ? s.LineGlyphStarts[i + 1] : glyphs.Length;
			for (int k = s.LineGlyphStarts[i]; k < end; k++)
				glyphs[k] = glyphs[k] with { X = glyphs[k].X + offsetX };
			lines[i] = lines[i] with { OffsetX = offsetX };
		}
	}

	/// <remarks>
	/// Negative for lines wider than the box that overflow it to the left.
	/// </remarks>
	private static float alignOffset(float lineWidth, float boxWidth, TextHorizontalAlign align, Direction direction) {
		float free = boxWidth - lineWidth;
		bool rtl = direction == Direction.RightToLeft;
		return align.Tag switch {
			TextHorizontalAlign.Case.Start => rtl ? free : 0f,
			TextHorizontalAlign.Case.Center => free * 0.5f,
			TextHorizontalAlign.Case.End => rtl ? 0f : free,
			TextHorizontalAlign.Case.Left => 0f,
			TextHorizontalAlign.Case.Right => free,
			_ => throw new UnreachableException(),
		};
	}
}

/// <summary>
/// Text laid out into positioned glyphs and lines, ready to draw with
/// <see cref="Canvas.Text(TextLayout, System.Numerics.Vector2, SrgbColor32?)"/>.
/// Obtained from <see cref="TextSystem.Layout(RichText, TextLayoutOptions?)"/> or
/// <see cref="TextSystem.Layout(string, in TextStyle, TextLayoutOptions?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Positions and sizes are in drawable units (with the identity transform), relative to the
/// layout's top-left corner. Glyphs are rasterized for the effective scale when drawn, so a layout
/// doesn't depend on e.g. the target it's drawn to.
/// </para>
/// <para>
/// This doesn't hold GPU resources, and doesn't need disposal. It does reference the font states
/// it was laid out with, though. If one of the fonts of the text's styles is an asset that gets
/// reloaded, the layout becomes <i>stale</i>, which is queryable through <see cref="IsStale"/>;
/// drawing it lays it out again first, which is also doable manually via
/// <see cref="RelayoutIfStale()"/>. Until it is re-laid-out, <see cref="Lines"/>,
/// <see cref="Width"/>, <see cref="Height"/>, and <see cref="IsTruncated"/> are out of date and still describe the previous
/// layout.
/// </para>
/// <para>
/// Not thread-safe; must be used on the same thread that uses its <see cref="TextSystem"/>.
/// </para>
/// </remarks>
public sealed class TextLayout {
	private readonly TextSystem owner;
	private ResolvedFontFallbackChain[] chains;
	private LaidOutGlyph[] glyphs;
	private ImmutableArray<TextLine> lines;

	internal TextLayout(TextSystem owner, RichText text, in TextLayoutOptions options) {
		this.owner = owner;
		Text = text;
		Options = options;
		(chains, glyphs, lines) = compute();
	}

	/// <summary>
	/// The laid out text. A plain string passed to
	/// <see cref="TextSystem.Layout(string, in TextStyle, TextLayoutOptions?)"/> is turned into rich
	/// text with a single span.
	/// </summary>
	public RichText Text { get; }

	/// <summary>
	/// How the text is broken into lines, aligned, and truncated.
	/// </summary>
	public TextLayoutOptions Options { get; }

	/// <summary>
	/// The lines, from top to bottom.
	/// </summary>
	/// <remarks>
	/// If the layout is stale (see <see cref="IsStale"/>), this still describes the previous layout.
	/// </remarks>
	public IReadOnlyList<TextLine> Lines => lines;

	/// <summary>
	/// Width of the widest line.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Lines are aligned within a box starting at X = 0 (see <see cref="TextHorizontalAlign"/>), so
	/// they can extend past this width: to the right if <see cref="TextLayoutOptions.MaxWidth"/> is
	/// finite and larger, and to the left (X &lt; 0) if a <see cref="TextHorizontalAlign.Center"/> or
	/// <see cref="TextHorizontalAlign.Right"/> line has a larger width than the box. If
	/// <see cref="TextLayoutOptions.MaxWidth"/> is positive infinity, this is the box's width, and
	/// every line is within [0, <see cref="Width"/>].
	/// </para>
	/// <para>
	/// If the layout is stale (see <see cref="IsStale"/>), this still describes the previous layout.
	/// </para>
	/// </remarks>
	public float Width { get; private set; }

	/// <summary>
	/// Distance from the top of the first line to the bottom of the last line's glyphs, i.e. the
	/// total height of the lines without the last line's <see cref="TextLine.LineGap"/>.
	/// </summary>
	/// <remarks>
	/// If the layout is stale (see <see cref="IsStale"/>), this still describes the previous layout.
	/// </remarks>
	public float Height { get; private set; }

	/// <summary>
	/// Whether any text was left out, i.e. whether any of <see cref="Lines"/> is truncated (see
	/// <see cref="TextLine.IsTruncated"/>).
	/// </summary>
	/// <remarks>
	/// If the layout is stale (see <see cref="IsStale"/>), this still describes the previous layout.
	/// </remarks>
	public bool IsTruncated { get; private set; }

	/// <summary>
	/// Whether one of the fonts of <see cref="Text"/>'s styles is an asset that was reloaded since the
	/// text was last laid out.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the <see cref="TextSystem"/> that made this layout has been disposed.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if one of the fonts is an asset and the calling thread isn't attached to its asset
	/// store.
	/// </exception>
	public bool IsStale {
		get {
			owner.ThrowIfDisposed();
			foreach (ResolvedFontFallbackChain chain in chains)
				if (chain.IsStale())
					return true;
			return false;
		}
	}

	/// <summary>
	/// Lays the text out again if the layout is stale (see <see cref="IsStale"/>).
	/// </summary>
	/// <returns>Whether the text was laid out again.</returns>
	/// <inheritdoc cref="IsStale" path="/exception"/>
	public bool RelayoutIfStale() {
		if (!IsStale)
			return false;
		(chains, glyphs, lines) = compute();
		return true;
	}

	internal TextSystem Owner => owner;
	internal ReadOnlySpan<ResolvedFontFallbackChain> Chains => chains;
	internal ReadOnlySpan<LaidOutGlyph> Glyphs => glyphs;

	private (ResolvedFontFallbackChain[], LaidOutGlyph[], ImmutableArray<TextLine>) compute() {
		// spans with equal fonts share a resolved chain, which also makes them shape as one run
		ReadOnlySpan<RichTextSpan> spans = Text.SpansSpan;
		Dictionary<FontFallbackChain, int> indices = new();
		List<ResolvedFontFallbackChain> resolved = new();
		var spanChains = new int[spans.Length];
		for (int i = 0; i < spans.Length; i++) {
			FontFallbackChain fonts = spans[i].Style.Fonts;
			if (!indices.TryGetValue(fonts, out int index)) {
				index = resolved.Count;
				indices.Add(fonts, index);
				resolved.Add(owner.ResolveFallbackChain(fonts));
			}
			spanChains[i] = index;
		}
		ResolvedFontFallbackChain[] chainArray = resolved.ToArray();
		LayoutResult r = owner.RunLayouter(chainArray, Text, spanChains, Options);
		Width = r.Width;
		Height = r.Height;
		IsTruncated = r.IsTruncated;
		return (chainArray, r.Glyphs, ImmutableArray.Create(r.Lines));
	}
}
