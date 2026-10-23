// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.Text;
using Injure.Primitives;

namespace Injure.Tests.Draw.Text;

public static class RichTextTests {
	// no shaping happens here so a dummy FontData works
	private static readonly FontFallbackChain fonts = new FontData([0, 1, 0, 0], null, 1);
	private static readonly FontFallbackChain otherFonts = new FontData([0, 1, 0, 0], null, 1);
	private static readonly TextStyle baseStyle = new(fonts, 10f);
	private static readonly TextStyleOverride red = new(Color: SrgbColor32.Red);

	private static (int Start, int Length, TextStyle Style)[] spans(RichText t) =>
		t.Spans.Select(static s => (s.Start, s.Length, s.Style)).ToArray();

	[Fact]
	public static void PushesNestAndPopsRestore() {
		RichText t = new RichTextBuilder(baseStyle)
			.Append("a")
			.Push(red)
			.Append("b")
			.Push(new TextStyleOverride(Size: 20f))
			.Append("c")
			.Pop()
			.Append("d")
			.Pop()
			.Append("e")
			.Build();
		Assert.Equal("abcde", t.Text);
		TextStyle redStyle = baseStyle with { Color = SrgbColor32.Red };
		Assert.Equal([
			(0, 1, baseStyle),
			(1, 1, redStyle),
			(2, 1, redStyle with { Size = 20f }),
			(3, 1, redStyle),
			(4, 1, baseStyle),
		], spans(t));
	}

	[Fact]
	public static void AdjacentEqualStylesMerge() {
		// an override that changes nothing, and a one-off override, both leave the style as is
		RichText t = new RichTextBuilder(baseStyle)
			.Append("a")
			.Push(new TextStyleOverride(Color: baseStyle.Color))
			.Append("b")
			.Pop()
			.Append("c", default)
			.Append("")
			.Build();
		Assert.Equal([(0, 3, baseStyle)], spans(t));
	}

	[Fact]
	public static void EmptyTextHasOneEmptySpan() {
		Assert.Equal([(0, 0, baseStyle)], spans(new RichTextBuilder(baseStyle).Build()));
		Assert.Equal([(0, 0, baseStyle)], spans(new RichText("", baseStyle)));
	}

	[Fact]
	public static void UnbalancedPushesAndPopsThrow() {
		Assert.Throws<InvalidOperationException>(() => new RichTextBuilder(baseStyle).Pop());
		Assert.Throws<InvalidOperationException>(() => new RichTextBuilder(baseStyle).Push(red).Build());
	}

	[Fact]
	public static void InvalidStylesAreRejected() {
		Assert.Throws<ArgumentInvalidDefaultException>(() => new RichTextBuilder(default));
		Assert.Throws<ArgumentOutOfRangeException>(() => new RichTextBuilder(baseStyle).Push(new TextStyleOverride(Size: 0f)));
		Assert.Throws<ArgumentNullException>(() => new RichText("a", baseStyle with { Fonts = null! }));
		Assert.Throws<ArgumentNullException>(() => new RichText("a", baseStyle with { Language = null! }));
		Assert.Throws<ArgumentNullException>(() => new RichText(null!, baseStyle));
	}

	[Fact]
	public static void SurrogatePairsAreNeverSplitBetweenSpans() {
		// the pair's halves are appended with different styles, so it takes the first half's
		RichText t = new RichTextBuilder(baseStyle).Append("a\ud83d").Append("\ude00b", red).Build();
		Assert.Equal("a😀b", t.Text);
		Assert.Equal([(0, 3, baseStyle), (3, 1, baseStyle with { Color = SrgbColor32.Red })], spans(t));
		// a span left empty by that disappears
		t = new RichTextBuilder(baseStyle).Append("a\ud83d").Append("\ude00", red).Append("b").Build();
		Assert.Equal([(0, 4, baseStyle)], spans(t));
		// unpaired halves are replaced
		Assert.Equal("a�", new RichTextBuilder(baseStyle).Append("a\ud83d").Build().Text);
	}

	[Fact]
	public static void RichTextsCompareByContent() {
		static RichText build(TextStyleOverride o) => new RichTextBuilder(baseStyle).Append("a").Append("b", o).Build();
		Assert.Equal(build(red), build(red));
		Assert.Equal(build(red).GetHashCode(), build(red).GetHashCode());
		Assert.NotEqual(build(red), build(new TextStyleOverride(Color: SrgbColor32.Green)));
		Assert.NotEqual(build(red), build(new TextStyleOverride(Fonts: otherFonts)));
		Assert.Equal(new RichText("ab", baseStyle), build(default));
		Assert.True(build(red) == build(red));
	}

	[Fact]
	public static void ColorOnlyChangesDontSplitShapingSpans() {
		RichText t = new RichTextBuilder(baseStyle)
			.Append("a")
			.Append("b", red)
			.Append("c", new TextStyleOverride(Size: 20f))
			.Append("d", new TextStyleOverride(Size: 20f, Color: SrgbColor32.Green))
			.Append("e", new TextStyleOverride(Language: "fr"))
			.Build();
		Assert.Equal(5, t.Spans.Count);
		// all spans use chain 0
		ShapingSpan[] shaping = TextLayouter.ShapingSpans(t, new int[t.Spans.Count]);
		Assert.Equal([(0, 2), (2, 4), (4, 5)], shaping.Select(static s => (s.Start, s.Limit)));
		// different fonts do split
		shaping = TextLayouter.ShapingSpans(t, [0, 1, 1, 1, 1]);
		Assert.Equal([(0, 1), (1, 2), (2, 4), (4, 5)], shaping.Select(static s => (s.Start, s.Limit)));
	}
}
