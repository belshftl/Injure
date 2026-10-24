// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;

namespace Injure.Tests.Draw.Text;

public static class TextLayoutTests {
	// one glyph per cluster, 10 wide
	private static ParagraphCluster c(int i, bool ws = false, LineBreakKind breakAfter = LineBreakKind.None) =>
		new(Run: 0, GlyphStart: i, GlyphCount: 1, SourceStart: i, SourceLength: 1, Width: 10, IsWhitespaceOnly: ws, UnsafeToBreak: false, BreakAfter: breakAfter);

	// `extra` stands in for a line getting wider when its ends are shaped again
	private static LineSpan[] wrap(ParagraphCluster[] clusters, float maxWidth, bool wrap = true, float extra = 0f) {
		List<LineSpan> spans = new();
		TextLayouter.Wrap(clusters, maxWidth, wrap, (start, end) => 10f * (end - start) + extra, spans);
		return spans.ToArray();
	}

	[Fact]
	public static void SkipLeadingWsWorks() =>
		Assert.Equal(2, TextLayouter.SkipLeadingWs([c(0, ws: true), c(1, ws: true), c(2)], 0));

	[Fact]
	public static void TrimTrailingWsWorks() =>
		Assert.Equal(1, TextLayouter.TrimTrailingWs([c(0), c(1, ws: true), c(2, ws: true)], 0, 3));

	[Fact]
	public static void EmptyParagraphsAreOneEmptyLine() =>
		Assert.Equal([new LineSpan(0, 0)], wrap([], 100f));

	[Fact]
	public static void WrapsAtTheLastBreakOpportunityAndTrimsSpacesAroundIt() {
		// "aa bbb", overflowing in the middle of "bbb", which has to move as a whole
		ParagraphCluster[] clusters = [c(0), c(1), c(2, ws: true, LineBreakKind.Soft), c(3), c(4), c(5)];
		Assert.Equal([new LineSpan(0, 2), new LineSpan(3, 6)], wrap(clusters, 45f));
		Assert.Equal([new LineSpan(0, 6)], wrap(clusters, 45f, wrap: false));
	}

	[Fact]
	public static void SpacesDontWrapByThemselves() {
		// "aa " at a width only "aa" fits in; the last line keeps its trailing space
		ParagraphCluster[] clusters = [c(0), c(1), c(2, ws: true, LineBreakKind.Soft)];
		Assert.Equal([new LineSpan(0, 3)], wrap(clusters, 20f));
	}

	[Fact]
	public static void WordsWiderThanTheLineBreakAnywhere() =>
		Assert.Equal([new LineSpan(0, 1), new LineSpan(1, 2), new LineSpan(2, 3)], wrap([c(0), c(1), c(2)], 15f));

	[Fact]
	public static void HardBreaksEndLines() =>
		Assert.Equal([new LineSpan(0, 2), new LineSpan(2, 3)], wrap([c(0), c(1, breakAfter: LineBreakKind.Hard), c(2)], 100f, wrap: false));

	[Fact]
	public static void LinesThatGetWiderWhenMeasuredBreakAtAnEarlierOpportunity() {
		// "a b cc"; by the clusters' widths, "a b" fits, but measured 25 wider, only single words do
		ParagraphCluster[] clusters = [c(0), c(1, ws: true, LineBreakKind.Soft), c(2), c(3, ws: true, LineBreakKind.Soft), c(4), c(5)];
		Assert.Equal([new LineSpan(0, 3), new LineSpan(4, 6)], wrap(clusters, 50f));
		Assert.Equal([new LineSpan(0, 1), new LineSpan(2, 3), new LineSpan(4, 6)], wrap(clusters, 50f, extra: 25f));
	}

	[Fact]
	public static void WordsThatGetWiderWhenMeasuredBreakWithinThemselves() {
		Assert.Equal([new LineSpan(0, 2), new LineSpan(2, 3)], wrap([c(0), c(1), c(2)], 30f, extra: 5f));
		// also before a hard break
		Assert.Equal(
			[new LineSpan(0, 2), new LineSpan(2, 3), new LineSpan(3, 4)],
			wrap([c(0), c(1), c(2, breakAfter: LineBreakKind.Hard), c(3)], 30f, extra: 5f)
		);
	}

	[Fact]
	public static void ClustersWiderThanTheLineStayOnTheirOwnLine() =>
		Assert.Equal([new LineSpan(0, 1), new LineSpan(1, 2)], wrap([c(0), c(1)], 5f, extra: 5f));
}
