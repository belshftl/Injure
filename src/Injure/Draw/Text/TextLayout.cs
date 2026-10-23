// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

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
/// A line wider than the box, e.g. a single word wider than a finite
/// <see cref="TextLayoutOptions.MaxWidth"/>, overflows it on the side or sides its alignment doesn't
/// hold in place: a <see cref="Left"/> line past the right edge, a <see cref="Right"/> line past the
/// left edge (to negative X coordinates), and a <see cref="Center"/> line equally past both.
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
		/// Lines start at the left edge.
		/// </summary>
		Left = 1,

		/// <summary>
		/// Lines are centered.
		/// </summary>
		Center,

		/// <summary>
		/// Lines end at the right edge.
		/// </summary>
		Right,
	}
}

/// <summary>
/// What happens to text past a maximum number of lines.
/// </summary>
/// <remarks>
/// <para>
/// Currently unused.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TextOverflowMode {
	/// <summary>Raw switch tag for <see cref="TextOverflowMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Lines past the maximum are left out.
		/// </summary>
		Clip = 1,

		/// <summary>
		/// Lines past the maximum are left out, and the last line ends in an ellipsis.
		/// </summary>
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
/// <remarks>
/// <para>
/// The vertical metrics are the largest of the line's fonts' metrics, each scaled by the size of
/// the text using it, or, for an empty line, those of the primary font and size at its position.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is an empty line at the origin with every
/// metric at 0.
/// </para>
/// </remarks>
public readonly record struct TextLine(
	float Width,
	float BaselineY,
	float Ascent,
	float Descent,
	float LineGap,
	float Height,
	float OffsetX
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
}

internal static class TextLayouter {
	private sealed class Scratch {
		public required RichText Text;
		public required ShapingSpan[] Shaping;
		public required FontLineMetrics[][] Metrics; // by chain, then font
		public readonly List<LaidOutGlyph> Glyphs = new();
		public readonly List<TextLine> Lines = new();
		// index of each line's first glyph in Glyphs
		public readonly List<int> LineGlyphStarts = new();
		public readonly List<ParagraphRun> Runs = new();
		public readonly List<ParagraphCluster> Clusters = new();
		public readonly List<LineSpan> Spans = new();
		public readonly List<LogicalBidiRun> LogicalRuns = new();
		public readonly List<VisualBidiRun> VisualRuns = new();
		public readonly List<ResolvedItem> Resolved = new();
		// libunibreak's language strings, and each paragraph's breaks for them
		public readonly Dictionary<string, string?> BreakLanguages = new(StringComparer.Ordinal);
		public readonly Dictionary<string, LineBreakOpportunity[]> ParagraphBreaks = new(StringComparer.Ordinal);
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
		Scratch s = new() { Text = text, Shaping = ShapingSpans(text, spanChains), Metrics = metrics };
		string str = text.Text;
		bool wrap = options.WrapMode == TextWrapMode.Greedy && !float.IsPositiveInfinity(options.MaxWidth);
		float top = 0f;
		float width = 0f;
		int paraStart = 0;
		for (;;) {
			int newline = str.IndexOf('\n', paraStart);
			int paraLimit = newline < 0 ? str.Length : newline;
			string para = str[paraStart..paraLimit];
			s.Runs.Clear();
			s.Clusters.Clear();
			s.Spans.Clear();
			if (para.Length == 0) {
				emitLine(s, null, paraStart, default, ref top, ref width);
			} else {
				using var analysis = TextAnalysis.ParagraphAnalysis.Create(para);
				buildRuns(s, itemizer, resolver, chains, para, paraStart, analysis);
				buildClusters(s, para, paraStart);
				Wrap(CollectionsMarshal.AsSpan(s.Clusters), options.MaxWidth, wrap, s.Spans);
				foreach (LineSpan span in s.Spans)
					emitLine(s, analysis, paraStart, span, ref top, ref width);
			}
			if (newline < 0)
				break;
			paraStart = newline + 1;
		}
		align(s, float.IsPositiveInfinity(options.MaxWidth) ? width : options.MaxWidth, options.HorizontalAlign);
		return new LayoutResult {
			Glyphs = s.Glyphs.ToArray(),
			Lines = s.Lines.ToArray(),
			Width = width,
			Height = top,
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

	private static void buildRuns(
		Scratch s,
		ITextItemizer itemizer,
		FallbackResolver resolver,
		ResolvedFontFallbackChain[] chains,
		string para,
		int paraStart,
		TextAnalysis.ParagraphAnalysis analysis
	) {
		ReadOnlySpan<ShapingSpan> shaping = s.Shaping;
		s.LogicalRuns.Clear();
		analysis.GetLogicalRuns(s.LogicalRuns);
		foreach (LogicalBidiRun bidiRun in s.LogicalRuns) {
			int runStart = paraStart + bidiRun.Start;
			int runLimit = runStart + bidiRun.Length;
			// each bidi run is cut further where the fonts, size, or language change
			for (int k = shapingSpanAt(shaping, runStart); k < shaping.Length && shaping[k].Start < runLimit; k++) {
				ShapingSpan span = shaping[k];
				int segStart = Math.Max(runStart, span.Start) - paraStart;
				int segLimit = Math.Min(runLimit, span.Limit) - paraStart;
				if (segLimit <= segStart)
					continue;
				TextItem[] items = itemizer.Itemize(para.AsSpan(segStart, segLimit - segStart), segStart, bidiRun.Direction, span.Language);
				foreach (TextItem item in items) {
					s.Resolved.Clear();
					resolver.ResolveItems(chains[span.Chain], item, s.Resolved);
					foreach (ResolvedItem r in s.Resolved) {
						s.Runs.Add(new ParagraphRun {
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

	private static void buildClusters(Scratch s, string para, int paraStart) {
		Span<LineBreakKind> breakAfter = (para.Length + 1) <= 2048
			? stackalloc LineBreakKind[para.Length + 1]
			: new LineBreakKind[para.Length + 1];
		breakAfter.Clear();
		// a break after a character follows that character's language
		// libunibreak takes a single language, so the paragraph is analyzed once per distinct language
		// in it, with full context
		ReadOnlySpan<ShapingSpan> shaping = s.Shaping;
		s.ParagraphBreaks.Clear();
		int paraLimit = paraStart + para.Length;
		for (int k = shapingSpanAt(shaping, paraStart); k < shaping.Length && shaping[k].Start < paraLimit; k++) {
			ShapingSpan span = shaping[k];
			string key = span.Language;
			if (!s.ParagraphBreaks.TryGetValue(key, out LineBreakOpportunity[]? breaks)) {
				if (!s.BreakLanguages.TryGetValue(key, out string? unibreakLang)) {
					unibreakLang = TextAnalysis.UnibreakLanguage(span.Language);
					s.BreakLanguages.Add(key, unibreakLang);
				}
				breaks = TextAnalysis.GetLineBreaks(para, unibreakLang);
				s.ParagraphBreaks.Add(key, breaks);
			}
			// breaks after the span's characters, i.e. at positions (start, limit] relative to the paragraph
			int lo = Math.Max(span.Start, paraStart) - paraStart;
			int hi = Math.Min(span.Limit, paraLimit) - paraStart;
			foreach (LineBreakOpportunity opp in breaks)
				if (opp.Position > lo && opp.Position <= hi)
					breakAfter[opp.Position] = opp.Kind;
		}
		for (int r = 0; r < s.Runs.Count; r++) {
			ParagraphRun run = s.Runs[r];
			foreach (ShapedCluster c in run.Shaped.SourceOrderClusters) {
				int start = run.Item.SourceStart + c.SourceStart;
				s.Clusters.Add(new ParagraphCluster(
					Run: r,
					GlyphStart: c.GlyphStart,
					GlyphCount: c.GlyphCount,
					SourceStart: start,
					SourceLength: c.SourceLength,
					Width: c.Width * run.Size,
					IsWhitespaceOnly: c.IsWhitespaceOnly,
					BreakAfter: breakAfter[start + c.SourceLength]
				));
			}
		}
	}

	/// <summary>
	/// Breaks a paragraph's clusters into lines; whitespace never causes a wrap by itself, and is
	/// trimmed from both sides of a wrap.
	/// </summary>
	public static void Wrap(ReadOnlySpan<ParagraphCluster> clusters, float maxWidth, bool wrap, List<LineSpan> dst) {
		if (clusters.IsEmpty) {
			dst.Add(default);
			return;
		}
		int start = 0;
		float width = 0f;
		int lastBreak = -1; // the last cluster of the line that a soft break may follow
		int i = 0;
		while (i < clusters.Length) {
			ParagraphCluster c = clusters[i];
			if (wrap && i > start && !c.IsWhitespaceOnly && width + c.Width > maxWidth) {
				int end = lastBreak >= start ? lastBreak + 1 : i;
				dst.Add(new LineSpan(start, TrimTrailingWs(clusters, start, end)));
				start = SkipLeadingWs(clusters, end);
				width = 0f;
				for (int k = start; k < i; k++)
					width += clusters[k].Width;
				lastBreak = -1;
				continue;
			}
			width += c.Width;
			if (c.BreakAfter == LineBreakKind.Hard) {
				dst.Add(new LineSpan(start, i + 1));
				start = i + 1;
				width = 0f;
				lastBreak = -1;
			} else if (c.BreakAfter == LineBreakKind.Soft) {
				lastBreak = i;
			}
			i++;
		}
		if (start < clusters.Length || dst.Count == 0)
			dst.Add(new LineSpan(start, clusters.Length));
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
	/// Lays out one line. Its vertical metrics are the largest of its runs' fonts' metrics scaled by
	/// the runs' sizes, or, for an empty line, those of the primary font and size at the paragraph's
	/// start.
	/// </summary>
	private static void emitLine(
		Scratch s,
		TextAnalysis.ParagraphAnalysis? analysis,
		int paraStart,
		LineSpan span,
		ref float top,
		ref float width
	) {
		ReadOnlySpan<ParagraphCluster> clusters = CollectionsMarshal.AsSpan(s.Clusters);
		float ascent = 0f, descent = 0f, lineGap = 0f;
		if (span.End > span.Start) {
			for (int k = span.Start; k < span.End; k++) {
				ParagraphRun run = s.Runs[clusters[k].Run];
				FontLineMetrics m = s.Metrics[run.Chain][run.FontIndex];
				ascent = MathF.Max(ascent, m.Ascent * run.Size);
				descent = MathF.Max(descent, m.Descent * run.Size);
				lineGap = MathF.Max(lineGap, m.LineGap * run.Size);
			}
		} else {
			ShapingSpan at = s.Shaping[shapingSpanAt(s.Shaping, paraStart)];
			FontLineMetrics m = s.Metrics[at.Chain][0];
			ascent = m.Ascent * at.Size;
			descent = m.Descent * at.Size;
			lineGap = m.LineGap * at.Size;
		}
		float baselineY = top + ascent;
		s.LineGlyphStarts.Add(s.Glyphs.Count);
		float penX = 0f;
		if (span.End > span.Start && analysis is not null) {
			s.VisualRuns.Clear();
			analysis.GetVisualRunsForLine(s.VisualRuns, clusters[span.Start].SourceStart, clusters[span.End - 1].SourceLimit);
			foreach (VisualBidiRun v in s.VisualRuns) {
				int a = lowerBound(clusters, span.Start, span.End, v.Start);
				int b = lowerBound(clusters, a, span.End, v.Start + v.Length);
				if (v.Direction == Direction.RightToLeft) {
					for (int k = b - 1; k >= a; k--)
						emitCluster(s, clusters[k], paraStart, baselineY, ref penX);
				} else {
					for (int k = a; k < b; k++)
						emitCluster(s, clusters[k], paraStart, baselineY, ref penX);
				}
			}
		}
		float height = ascent + descent + lineGap;
		s.Lines.Add(new TextLine(
			Width: penX,
			BaselineY: baselineY,
			Ascent: ascent,
			Descent: descent,
			LineGap: lineGap,
			Height: height,
			OffsetX: 0f // set by align()
		));
		top += height;
		width = MathF.Max(width, penX);
	}

	// each glyph takes the color of its cluster's first character
	private static void emitCluster(Scratch s, in ParagraphCluster c, int paraStart, float baselineY, ref float penX) {
		ParagraphRun run = s.Runs[c.Run];
		ReadOnlySpan<RichTextSpan> spans = s.Text.SpansSpan;
		ReadOnlySpan<ShapedGlyph> glyphs = run.Shaped.Glyphs.AsSpan(c.GlyphStart, c.GlyphCount);
		float size = run.Size;
		foreach (ShapedGlyph g in glyphs) {
			int source = paraStart + run.Item.SourceStart + (int)g.Cluster;
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

	/// <summary>
	/// Offsets each line horizontally to align it within a box of width <paramref name="boxWidth"/>
	/// starting at x = 0.
	/// </summary>
	private static void align(Scratch s, float boxWidth, TextHorizontalAlign align) {
		Span<TextLine> lines = CollectionsMarshal.AsSpan(s.Lines);
		Span<LaidOutGlyph> glyphs = CollectionsMarshal.AsSpan(s.Glyphs);
		for (int i = 0; i < lines.Length; i++) {
			float offsetX = alignOffset(lines[i].Width, boxWidth, align);
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
	private static float alignOffset(float lineWidth, float boxWidth, TextHorizontalAlign align) {
		float free = boxWidth - lineWidth;
		return align.Tag switch {
			TextHorizontalAlign.Case.Left => 0f,
			TextHorizontalAlign.Case.Center => free * 0.5f,
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
/// <see cref="Width"/>, and <see cref="Height"/> are out of date and still describe the previous
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
	/// How the text is broken into lines and aligned.
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
	/// Total height of the lines.
	/// </summary>
	/// <remarks>
	/// If the layout is stale (see <see cref="IsStale"/>), this still describes the previous layout.
	/// </remarks>
	public float Height { get; private set; }

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
		return (chainArray, r.Glyphs, ImmutableArray.Create(r.Lines));
	}
}
