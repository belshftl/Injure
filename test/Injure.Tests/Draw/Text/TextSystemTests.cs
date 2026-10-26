// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Draw;
using Injure.Draw.Text;
using Injure.Gpu;
using Injure.Mods;
using Injure.Primitives;
using Injure.Tests.Assets;
using Injure.Tests.Gpu;

namespace Injure.Tests.Draw.Text;

// the fonts come from Fonts/make_test_fonts.py
//
// every glyph is a box spanning 50 units in from either side of its advance and 700 units up from
// the baseline, letters advance 500 units ('m' 1000), the wide variant's letters 700, digits 600,
// space 300, .notdef 500, with an ascent of 800, a descent of 200, and 1000 units per em, so at
// size 10 everything is whole drawable units
//
// the gap variant has a line gap of 200
// the contextual variant has 'b' before 'c' and 'c' after 'b' advance 300
// the ellipsis variant adds an 800-unit U+2026
// the dots variant adds a 200-unit '.' and a 300-unit '-'
// the hyphen variant adds a 400-unit U+2010, a 300-unit '-', and an 800-unit U+2026
public sealed class TextSystemTests : GpuTestBase {
	// layout uses ijtext; the native libs collection can't be used here, since this class is already
	// in the GPU one
	public TextSystemTests(GpuDeviceFixture fixture) : base(fixture) => NativeLibsLoader.Init();

	private static byte[] fontBytes(string name) {
		using Stream s = typeof(TextSystemTests).Assembly.GetManifestResourceStream($"fonts/{name}.ttf")
			?? throw new InvalidOperationException($"test font {name} is missing");
		using MemoryStream ms = new();
		s.CopyTo(ms);
		return ms.ToArray();
	}

	// unhinted, so box edges are rasterized exactly where the outlines put them
	private static FontSpec spec(FontData font, FontRasterMode mode = default) =>
		new(font, raster: new FontRasterOptions(FontHinting.None, mode));

	private static TextStyle style(FontFallbackChain fonts, float size = 10f, SrgbColor32? color = null, string language = "und") =>
		new(fonts, size, color, language);

	private byte[] render(uint w, uint h, float pixelsPerUnit, Action<OwnedCanvas> draw, TextSystem? text = null) {
		using GpuTexture target = GpuRig.Target(Device, w, h);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			var p = CanvasParams.Primary(Viewport.Whole(new Vector2(w, h), pixelsPerUnit), SrgbColor32.Black);
			using (OwnedCanvas canvas = new(frame, in p, text))
				draw(canvas);
			frame.Submit();
		}
		return GpuRig.Read(Device, target);
	}

	[Fact]
	public void LoadFontRejectsNonFontsAndDisposedSystems() {
		TextSystem text = new(Device);
		Assert.Throws<ArgumentException>(() => text.LoadFont([1, 2, 3, 4, 5, 6, 7, 8]));
		Assert.Throws<ArgumentException>(() => text.LoadFont([]));
		// WOFF loads in FreeType, but HarfBuzz can't shape it, so it's rejected up front
		Assert.Throws<ArgumentException>(() => text.LoadFont([(byte)'w', (byte)'O', (byte)'F', (byte)'F', 0, 0, 0, 0]));
		// the header check passes, but there's no table directory after it
		FtException ft = Assert.Throws<FtException>(() => text.LoadFont([0, 1, 0, 0, 0, 0, 0, 0]));
		Assert.Equal("FT_New_Memory_Face", ft.Function);
		Assert.StartsWith("FT_Err_", ft.Error);
		// font asset creators call this from loading threads
		Exception? caught = null;
		FontData? loaded = null;
		Thread t = new(() => caught = Record.Exception(() => loaded = text.LoadFont(fontBytes("InjureTestSans"), "sans")));
		t.Start();
		t.Join();
		Assert.Null(caught);
		Assert.Equal(1, loaded!.FaceCount);
		Assert.Equal("sans", loaded.DebugName);
		text.Dispose();
		text.Dispose();
		Assert.Throws<ObjectDisposedException>(() => text.LoadFont(fontBytes("InjureTestSans")));
	}

	[Fact]
	public void LayoutUsesUnhintedAdvancesAndLineMetrics() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout l = text.Layout("aaa", style(sans));
		Assert.Equal(15f, l.Width);
		Assert.Equal(10f, l.Height);
		TextLine line = Assert.Single(l.Lines);
		Assert.Equal(8f, line.BaselineY);
		Assert.Equal(15f, text.Layout("am", style(sans)).Width);
		Assert.Equal(13f, text.Layout("a a", style(sans)).Width);
		// sizes scale linearly, without integer rounding
		Assert.Equal(7.5f, text.Layout("aaa", style(sans, 5f)).Width);
		Assert.Equal(1.5f * 0.5f * 3, text.Layout("aaa", style(sans, 1.5f)).Width, 5);
	}

	[Fact]
	public void LayoutRejectsInvalidStylesAndOptions() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		Assert.Throws<ArgumentInvalidDefaultException>(() => text.Layout("a", default));
		Assert.Equal("style.Fonts", Assert.Throws<ArgumentNullException>(() => text.Layout("a", style(sans) with { Fonts = null! })).ParamName);
		Assert.Throws<ArgumentOutOfRangeException>(() => text.Layout("a", style(sans, 0f)));
		Assert.Throws<ArgumentOutOfRangeException>(() => text.Layout("a", style(sans, float.NaN)));
		Assert.Throws<ArgumentOutOfRangeException>(() => text.Layout("a", style(sans), TextLayoutOptions.Default with { MaxWidth = -1 }));
		// nested defaults are reported by their path
		ArgumentInvalidDefaultException e = Assert.Throws<ArgumentInvalidDefaultException>(() => text.Layout("a", style(sans), TextLayoutOptions.Default with { WrapMode = default }));
		Assert.Equal("options.WrapMode", e.ParamName);
		e = Assert.Throws<ArgumentInvalidDefaultException>(() => text.Layout("a", style(sans), TextLayoutOptions.Default with { HorizontalAlign = default }));
		Assert.Equal("options.HorizontalAlign", e.ParamName);
		e = Assert.Throws<ArgumentInvalidDefaultException>(() => text.Layout("a", style(sans), default(TextLayoutOptions)));
		Assert.Equal("options", e.ParamName);
		e = Assert.Throws<ArgumentInvalidDefaultException>(() => text.Layout("a", style(sans), TextLayoutOptions.Default with { HorizontalOverflow = default }));
		Assert.Equal("options.HorizontalOverflow", e.ParamName);
		e = Assert.Throws<ArgumentInvalidDefaultException>(() => text.Layout("a", style(sans), TextLayoutOptions.Default with { TruncationMarker = default }));
		Assert.Equal("options.TruncationMarker", e.ParamName);
		ArgumentOutOfRangeException r = Assert.Throws<ArgumentOutOfRangeException>(() => text.Layout("a", style(sans), TextLayoutOptions.Default with { MaxLines = 0 }));
		Assert.Equal("options.MaxLines", r.ParamName);
		r = Assert.Throws<ArgumentOutOfRangeException>(() => text.Layout("a", style(sans), TextLayoutOptions.Default with { MaxHeight = -1 }));
		Assert.Equal("options.MaxHeight", r.ParamName);
		Assert.Throws<ArgumentOutOfRangeException>(() => text.Layout("a", style(sans), TextLayoutOptions.Default with { MaxHeight = float.NaN }));
	}

	[Fact]
	public void GreedyWrappingBreaksAtSpaces() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout l = text.Layout("aa aa aa", style(sans), TextLayoutOptions.Default with { MaxWidth = 26 });
		Assert.Equal(2, l.Lines.Count);
		// spaces at a wrap don't count towards a line's width
		Assert.Equal(23f, l.Lines[0].Width);
		Assert.Equal(10f, l.Lines[1].Width);
		Assert.Equal(18f, l.Lines[1].BaselineY);
		Assert.Equal(20f, l.Height);
		Assert.Single(text.Layout("aa aa aa", style(sans)).Lines);
		Assert.Single(text.Layout("aa aa aa", style(sans), TextLayoutOptions.Default with { MaxWidth = 26, WrapMode = TextWrapMode.None }).Lines);
	}

	[Fact]
	public void LineEndsAreShapedAgainIfABreakChangesContextualForms() {
		using TextSystem text = new(Device);
		FontData ctx = text.LoadFont(fontBytes("InjureTestContextual"));
		// unbroken, 'b' and 'c' use their 300-unit forms
		Assert.Equal(11f, text.Layout("abc", style(ctx)).Width);
		float[] widths(float maxWidth) =>
			text.Layout("abc", style(ctx), TextLayoutOptions.Default with { MaxWidth = maxWidth }).Lines.Select(static l => l.Width).ToArray();
		// "ab" fits by its unbroken width, and broken apart, both sides go back to the usual forms
		Assert.Equal([10f, 5f], widths(10));
		// "ab" no longer fits once broken from 'c', so the break moves back
		Assert.Equal([5f, 6f], widths(9));
		// same goes for truncation
		TextLayoutOptions cut = TextLayoutOptions.Default with {
			MaxWidth = 9,
			WrapMode = TextWrapMode.None,
			HorizontalOverflow = TextHorizontalOverflow.Cut,
			TruncationMarker = TextTruncationMarker.None,
		};
		Assert.Equal(5f, text.Layout("abc", style(ctx), cut).Width);
	}

	[Fact]
	public void HeightDoesntIncludeTheLastLinesLineGap() {
		using TextSystem text = new(Device);
		FontData gap = text.LoadFont(fontBytes("InjureTestSansGap"));
		TextLayout l = text.Layout("a\na", style(gap));
		Assert.Equal(12f, l.Lines[0].Height);
		Assert.Equal(20f, l.Lines[1].BaselineY);
		Assert.Equal(22f, l.Height);
	}

	private static float[] widths(TextLayout l) => l.Lines.Select(static line => line.Width).ToArray();

	private static bool[] truncated(TextLayout l) => l.Lines.Select(static line => line.IsTruncated).ToArray();

	[Fact]
	public void LinesPastMaxLinesAreDroppedAndTheLastKeptOneEndsInAnEllipsis() {
		using TextSystem text = new(Device);
		FontData ell = text.LoadFont(fontBytes("InjureTestEllipsis"));
		TextLayout l = text.Layout("aa\nbb\ncc", style(ell), TextLayoutOptions.Default with { MaxLines = 2 });
		Assert.Equal([10f, 18f], widths(l));
		Assert.Equal([false, true], truncated(l));
		Assert.True(l.IsTruncated);
		Assert.Equal(20f, l.Height);
		l = text.Layout("aa\nbb\ncc", style(ell), TextLayoutOptions.Default with { MaxLines = 3 });
		Assert.Equal([10f, 10f, 10f], widths(l));
		Assert.False(l.IsTruncated);
		// empty lines count too
		Assert.Equal([18f], widths(text.Layout("aa\n", style(ell), TextLayoutOptions.Default with { MaxLines = 1 })));
		// the line stays as-is with no marker
		l = text.Layout("aa \nbb", style(ell), TextLayoutOptions.Default with { MaxLines = 1, TruncationMarker = TextTruncationMarker.None });
		Assert.Equal([13f], widths(l));
		Assert.True(l.IsTruncated);
	}

	[Fact]
	public void TheEllipsisReplacesTextToFitTheBox() {
		using TextSystem text = new(Device);
		FontData ell = text.LoadFont(fontBytes("InjureTestEllipsis"));
		TextLayoutOptions opts = TextLayoutOptions.Default with { MaxWidth = 25, MaxLines = 1 };
		// the first line is "aaaa", and "aaaa…" is 28 wide
		Assert.Equal([23f], widths(text.Layout("aaaa aaaa", style(ell), opts)));
		// "aa …" would fit, but the space before the ellipsis is dropped
		Assert.Equal([18f], widths(text.Layout("aa a\nb", style(ell), opts with { MaxWidth = 21 })));
		// with Overflow, a line already wider than the box gets the ellipsis after all of its text
		opts = opts with { MaxWidth = 10, WrapMode = TextWrapMode.None };
		Assert.Equal([38f], widths(text.Layout("aaaaaa\nb", style(ell), opts)));
		// and an ellipsis wider than the box replaces all of the text
		Assert.Equal([8f], widths(text.Layout("a\nb", style(ell), opts with { MaxWidth = 6 })));
	}

	[Fact]
	public void LinesStopAtMaxHeight() {
		using TextSystem text = new(Device);
		FontData ell = text.LoadFont(fontBytes("InjureTestEllipsis"));
		// "a a" wraps into two lines, and lines are 10 apart; the second line's bottom is at 20
		TextLayoutOptions opts = TextLayoutOptions.Default with { MaxWidth = 5, TruncationMarker = TextTruncationMarker.None };
		int lines(float maxHeight, int? maxLines = null) =>
			text.Layout("a a\na", style(ell), opts with { MaxHeight = maxHeight, MaxLines = maxLines }).Lines.Count;
		Assert.Equal(3, lines(30));
		Assert.Equal(2, lines(20));
		Assert.Equal(1, lines(19.5f));
		// the first line is always kept
		Assert.Equal(1, lines(0));
		Assert.True(text.Layout("a a\na", style(ell), opts with { MaxHeight = 0 }).IsTruncated);
		// with both limits, whichever is reached first applies
		Assert.Equal(1, lines(30, maxLines: 1));
		Assert.Equal(2, lines(20, maxLines: 3));
		Assert.Equal(3, lines(float.PositiveInfinity));
	}

	[Fact]
	public void CutTruncatesLinesWiderThanTheBox() {
		using TextSystem text = new(Device);
		FontData ell = text.LoadFont(fontBytes("InjureTestEllipsis"));
		TextLayoutOptions opts = TextLayoutOptions.Default with { MaxWidth = 22, WrapMode = TextWrapMode.None, HorizontalOverflow = TextHorizontalOverflow.Cut };
		// "aaa…" is 23 wide, so only "aa" fits next to the ellipsis
		TextLayout l = text.Layout("aaaaaa\naa", style(ell), opts);
		Assert.Equal([18f, 10f], widths(l));
		Assert.Equal([true, false], truncated(l));
		Assert.Equal([20f], widths(text.Layout("aaaaaa", style(ell), opts with { TruncationMarker = TextTruncationMarker.None })));
		// an ellipsis wider than the box is left out
		Assert.Equal([5f], widths(text.Layout("aaaaaa", style(ell), opts with { MaxWidth = 7 })));
		// with Greedy, a single cluster wider than the box is cut entirely
		l = text.Layout("m", style(ell), opts with { MaxWidth = 9, WrapMode = TextWrapMode.Greedy, TruncationMarker = TextTruncationMarker.None });
		Assert.Equal([0f], widths(l));
		Assert.True(l.IsTruncated);
		Assert.False(text.Layout("aaaaaa", style(ell), opts with { MaxWidth = float.PositiveInfinity }).IsTruncated);
	}

	[Fact]
	public void TheEllipsisGoesAtTheEndOfTheLineInItsParagraphsDirection() {
		using TextSystem text = new(Device);
		FontData ell = text.LoadFont(fontBytes("InjureTestEllipsis"));
		TextLayoutOptions opts = TextLayoutOptions.Default with { MaxLines = 1 };
		// the ellipsis's glyph is the one whose source index is the first dropped character
		// here that's the '\n'
		TextLayout ltr = text.Layout("ab\nc", style(ell), opts);
		Assert.Equal(2, ltr.Glyphs[^1].SourceIndex);
		Assert.Equal(10f, ltr.Glyphs[^1].X);
		// the font doesn't have hebrew so these become .notdefs
		TextLayout rtl = text.Layout("אב\nc", style(ell), opts);
		Assert.Equal(2, rtl.Glyphs[0].SourceIndex);
		Assert.Equal(0f, rtl.Glyphs[0].X);
		Assert.Equal(18f, rtl.Width);
	}

	[Fact]
	public void TheEllipsisTakesTheStyleOfTheLastKeptCharacter() {
		using TextSystem text = new(Device);
		FontData ell = text.LoadFont(fontBytes("InjureTestEllipsis"));
		RichText t = new RichTextBuilder(style(ell))
			.Append("aa", new TextStyleOverride(Color: SrgbColor32.Red))
			.Append("aaaa", new TextStyleOverride(Color: SrgbColor32.Blue, Size: 20f))
			.Build();
		TextLayoutOptions opts = TextLayoutOptions.Default with { MaxWidth = 30, WrapMode = TextWrapMode.None, HorizontalOverflow = TextHorizontalOverflow.Cut };
		// "aa" + a 20-unit 'a' + a 16-unit ellipsis is 46 wide, so only "aa" is kept, with a 10-size red ellipsis
		TextLayout l = text.Layout(t, opts);
		Assert.Equal([18f], widths(l));
		Assert.Equal(SrgbColor32.Red, l.Glyphs[^1].Color);
		// a wider box keeps blue 'a's, so the ellipsis is blue and at size 20
		l = text.Layout(t, opts with { MaxWidth = 47 });
		Assert.Equal([46f], widths(l));
		Assert.Equal(SrgbColor32.Blue, l.Glyphs[^1].Color);
	}

	[Fact]
	public void TheEllipsisPrefersThePrimaryFont() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		FontData ell = text.LoadFont(fontBytes("InjureTestEllipsis"));
		FontData dots = text.LoadFont(fontBytes("InjureTestDots"));
		float ellipsisWidth(FontFallbackChain fonts) =>
			text.Layout("a\nb", style(fonts), TextLayoutOptions.Default with { MaxLines = 1 }).Width - 5f;
		Assert.Equal(8f, ellipsisWidth(ell));
		// "..." from the primary font over U+2026 from a fallback
		Assert.Equal(6f, ellipsisWidth(new FontFallbackChain(dots, ell)));
		// U+2026 from a fallback over "..." from an earlier one
		Assert.Equal(8f, ellipsisWidth(new FontFallbackChain(sans, dots, ell)));
		Assert.Equal(6f, ellipsisWidth(new FontFallbackChain(sans, dots)));
		// if neither are found anywhere "..." is three .notdefs
		Assert.Equal(15f, ellipsisWidth(sans));
	}

	[Fact]
	public void LinesBreakingAtSoftHyphensEndInAHyphen() {
		using TextSystem text = new(Device);
		FontData hy = text.LoadFont(fontBytes("InjureTestHyphen"));
		// "aa aaa" plus the 4-wide U+2010 just fits 32
		TextLayout l = text.Layout("aa aaa\u00ADaa", style(hy), TextLayoutOptions.Default with { MaxWidth = 32 });
		Assert.Equal([32f, 10f], widths(l));
		// the hyphen's glyph has the soft hyphen's source index, and comes after the soft hyphen's own
		// zero-width glyph
		LaidOutGlyph[] fromSoftHyphen = l.Glyphs.ToArray().Where(static g => g.SourceIndex == 6).ToArray();
		Assert.Equal(2, fromSoftHyphen.Length);
		Assert.Equal(28f, fromSoftHyphen[1].X);
		// it counts towards the width, so at 30, the line breaks at the space instead
		Assert.Equal([10f, 25f], widths(text.Layout("aa aaa\u00ADaa", style(hy), TextLayoutOptions.Default with { MaxWidth = 30 })));
		// soft hyphens that don't end a line aren't drawn
		Assert.Equal(20f, text.Layout("aa\u00ADaa", style(hy)).Width);
	}

	[Fact]
	public void TheHyphenGoesAfterTheSoftHyphenInItsRunsDirection() {
		using TextSystem text = new(Device);
		FontData hy = text.LoadFont(fontBytes("InjureTestHyphen"));
		// the font doesn't have arabic so there become .notdefs; the first line is two letters plus
		// the hyphen, which goes on their left
		// use arabic rather than hebrew to test because a line can't break after a hebrew letter and a
		// soft hyphen
		TextLayout l = text.Layout("\u0627\u0628\u00AD\u062A\u062B", style(hy), TextLayoutOptions.Default with { MaxWidth = 15 });
		Assert.Equal([14f, 10f], widths(l));
		Assert.Equal(2, l.Glyphs[0].SourceIndex);
		Assert.Equal(l.Lines[0].OffsetX, l.Glyphs[0].X);
	}

	[Fact]
	public void TruncatedLinesKeepTheirHyphenUnlessTextIsDroppedForTheEllipsis() {
		using TextSystem text = new(Device);
		FontData hy = text.LoadFont(fontBytes("InjureTestHyphen"));
		// the first line is "aaa" plus the hyphen; with the ellipsis, that's 27 wide
		TextLayoutOptions opts = TextLayoutOptions.Default with { MaxWidth = 27, MaxLines = 1 };
		Assert.Equal([27f], widths(text.Layout("aaa\u00ADaaaa", style(hy), opts)));
		Assert.Equal([19f], widths(text.Layout("aaa\u00ADaaaa", style(hy), opts with { TruncationMarker = TextTruncationMarker.None })));
		// at 26, the hyphen doesn't fit, so the soft hyphen is dropped for the ellipsis, taking the
		// hyphen with it
		Assert.Equal([23f], widths(text.Layout("aaa\u00ADaaaa", style(hy), opts with { MaxWidth = 26 })));
	}

	[Fact]
	public void TheHyphenPrefersThePrimaryFont() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		FontData hy = text.LoadFont(fontBytes("InjureTestHyphen"));
		FontData dots = text.LoadFont(fontBytes("InjureTestDots"));
		// the first line is "aaa" and the hyphen
		float hyphenWidth(FontFallbackChain fonts) =>
			text.Layout("aaa\u00ADaaa", style(fonts), TextLayoutOptions.Default with { MaxWidth = 20 }).Lines[0].Width - 15f;
		Assert.Equal(4f, hyphenWidth(hy));
		Assert.Equal(3f, hyphenWidth(new FontFallbackChain(dots, hy)));
		// only '-' is tried from fallbacks, even if one has U+2010
		Assert.Equal(3f, hyphenWidth(new FontFallbackChain(sans, hy)));
		// with neither anywhere, '-' is a .notdef
		Assert.Equal(5f, hyphenWidth(sans));
	}

	private static TextLine[] aligned(TextSystem text, FontData sans, string s, float maxWidth, TextHorizontalAlign align) =>
		text.Layout(s, style(sans), TextLayoutOptions.Default with { MaxWidth = maxWidth, WrapMode = TextWrapMode.None, HorizontalAlign = align }).Lines.ToArray();

	[Fact]
	public void TheLanguageSelectsLineBreakingRules() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// the font has no hangul, so each syllable is a 500-unit .notdef, and "가나 다라마" fits the box
		// up to "다"; by default, korean breaks between any syllables, and with keep-all only at spaces
		const string s = "가나 다라마";
		TextLayoutOptions box = TextLayoutOptions.Default with { MaxWidth = 18 };
		float[] widths(string language) =>
			text.Layout(s, style(sans, language: language), box).Lines.Select(static l => l.Width).ToArray();
		Assert.Equal([18f, 10f], widths("und"));
		Assert.Equal([18f, 10f], widths("ko"));
		Assert.Equal([10f, 15f], widths("ko-u-lw-keepall"));
	}

	[Fact]
	public void AlignmentOffsetsLines() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		Assert.Equal(0f, aligned(text, sans, "aa", 20, TextHorizontalAlign.Left)[0].OffsetX);
		Assert.Equal(5f, aligned(text, sans, "aa", 20, TextHorizontalAlign.Center)[0].OffsetX);
		Assert.Equal(10f, aligned(text, sans, "aa", 20, TextHorizontalAlign.Right)[0].OffsetX);
	}

	[Fact]
	public void WithoutAMaxWidthLinesAlignWithinTheWidestLine() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// "aaaa" is 20 wide and "aa" 10
		TextLine[] center = aligned(text, sans, "aaaa\naa", float.PositiveInfinity, TextHorizontalAlign.Center);
		Assert.Equal(0f, center[0].OffsetX);
		Assert.Equal(5f, center[1].OffsetX);
		TextLine[] right = aligned(text, sans, "aaaa\naa", float.PositiveInfinity, TextHorizontalAlign.Right);
		Assert.Equal(0f, right[0].OffsetX);
		Assert.Equal(10f, right[1].OffsetX);
		Assert.Equal(20f, text.Layout("aaaa\naa", style(sans)).Width);
		Assert.Equal(0f, aligned(text, sans, "aaaa\naa", float.PositiveInfinity, TextHorizontalAlign.Left)[1].OffsetX);
	}

	[Fact]
	public void OverflowingLinesOverflowOnTheSidesTheirAlignmentLeavesOpen() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// "aaaa" is 20 wide, in a 10-wide box
		Assert.Equal(0f, aligned(text, sans, "aaaa", 10, TextHorizontalAlign.Left)[0].OffsetX);
		Assert.Equal(-5f, aligned(text, sans, "aaaa", 10, TextHorizontalAlign.Center)[0].OffsetX);
		Assert.Equal(-10f, aligned(text, sans, "aaaa", 10, TextHorizontalAlign.Right)[0].OffsetX);
		// the glyphs move with the line; drawn at 15.5, the line starts at 5.5, and 'a' starts 0.5 in
		// from its origin, so the boxes cover exactly pixels 6..9 through 21..24
		byte[] px = render(32, 12, 1f, oc => oc.Canvas.Text(text.Layout("aaaa", style(sans), TextLayoutOptions.Default with { MaxWidth = 10, WrapMode = TextWrapMode.None, HorizontalAlign = TextHorizontalAlign.Right }), new Vector2(15.5f, 0f), SrgbColor32.White));
		Assert.Equal(0, GpuRig.At(px, 32, 5, 4).R);
		Assert.Equal(255, GpuRig.At(px, 32, 6, 4).R);
		Assert.Equal(255, GpuRig.At(px, 32, 24, 4).R);
		Assert.Equal(0, GpuRig.At(px, 32, 25, 4).R);
	}

	[Fact]
	public void ParagraphsAndEmptyLinesStack() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout l = text.Layout("a\n\nam", style(sans));
		Assert.Equal(3, l.Lines.Count);
		Assert.Equal(0f, l.Lines[1].Width);
		Assert.Equal(28f, l.Lines[2].BaselineY);
		Assert.Equal(15f, l.Width);
		Assert.Equal(30f, l.Height);
	}

	[Fact]
	public void ParagraphAndLineSeparatorsEndLinesWithoutBeingDrawn() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout l = text.Layout("a\r\nb\rc\u0085d\u2029e\u2028f\vg\fh", style(sans));
		Assert.Equal([5f, 5f, 5f, 5f, 5f, 5f, 5f, 5f], widths(l));
		Assert.Equal(8, l.Glyphs.Length);
		// two line separators in a row make an empty line, and one at the end starts one too
		Assert.Equal([5f, 0f, 5f, 0f], widths(text.Layout("a\u2028\u2028b\u2028", style(sans))));
	}

	[Fact]
	public void EmptyLinesAtTheEndOfAParagraphTakeTheSizeThere() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		RichText t = new RichTextBuilder(style(sans)).Append("a").Append("\u2028", new TextStyleOverride(Size: 20f)).Build();
		Assert.Equal(16f, text.Layout(t).Lines[1].Ascent);
	}

	[Fact]
	public void StartAndEndFollowParagraphDirection() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// the font doesn't have hebrew so these become .notdefs; a paragraph separator starts an ltr
		// paragraph, while a line separator stays in the existing rtl one
		float[] offsets(string s, TextHorizontalAlign align) =>
			text.Layout(s, style(sans), TextLayoutOptions.Default with { MaxWidth = 20, HorizontalAlign = align })
				.Lines.Select(static l => l.OffsetX).ToArray();
		Assert.Equal([10f, 0f], offsets("אב\u2029ab", TextHorizontalAlign.Start));
		Assert.Equal([0f, 10f], offsets("אב\u2029ab", TextHorizontalAlign.End));
		Assert.Equal([10f, 10f], offsets("אב\u2028ab", TextHorizontalAlign.Start));
		Assert.Equal([0f, 0f], offsets("אב\u2028ab", TextHorizontalAlign.Left));
		Assert.Equal([10f, 10f], offsets("אב\u2029ab", TextHorizontalAlign.Right));
	}

	[Fact]
	public void UnpairedSurrogatesAreReplacedWithReplacementCharacters() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// the test font has no U+FFFD, so it's a 500-unit .notdef
		TextLayout l = text.Layout("a\uD800b", style(sans));
		Assert.Equal(15f, l.Width);
		Assert.Equal("a�b", l.Text.Text);
		TextLayout lone = text.Layout("\uDC00", style(sans));
		Assert.Equal("�", lone.Text.Text);
		Assert.Equal(5f, lone.Width);
		// valid text is kept as is, without a copy
		string valid = "ab😀";
		Assert.Same(valid, text.Layout(valid, style(sans)).Text.Text);
		// the layout cache still finds the layout by the text as given
		TextLayout cached = text.GetCachedLayout("a\uD800b", style(sans), TextLayoutOptions.Default);
		Assert.Equal("a�b", cached.Text.Text);
		Assert.Same(cached, text.GetCachedLayout("a\uD800b", style(sans), TextLayoutOptions.Default));
	}

	[Fact]
	public void RightToLeftTextIsLaidOutInVisualOrder() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// the glyphs are all .notdefs since the font doesn't have hebrew, tell apart by source indices
		static int[] visualOrder(TextLayout l) =>
			l.Glyphs.ToArray().OrderBy(static g => g.X).Select(static g => g.SourceIndex).ToArray();
		// ltr paragraph with an rtl run
		Assert.Equal([0, 1, 2, 4, 3], visualOrder(text.Layout("ab אב", style(sans))));
		// rtl paragraph with an ltr run
		Assert.Equal([3, 4, 2, 1, 0], visualOrder(text.Layout("אב ab", style(sans))));
	}

	[Fact]
	public void MixedSizesShareABaselineAndTakeTheLargestMetrics() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		RichText t = new RichTextBuilder(style(sans)).Append("a").Append("a", new TextStyleOverride(Size: 20f)).Build();
		TextLayout l = text.Layout(t);
		TextLine line = Assert.Single(l.Lines);
		// size 20's ascent of 16 and descent of 4 win over size 10's 8 and 2
		Assert.Equal((16f, 4f, 20f), (line.Ascent, line.Descent, line.Height));
		Assert.Equal(15f, l.Width);
		Assert.All(l.Glyphs.ToArray(), static g => Assert.Equal(16f, g.Y));
		Assert.Equal([10f, 20f], l.Glyphs.ToArray().Select(static g => g.Size));
	}

	[Fact]
	public void EmptyLinesTakeTheSizeAtTheirPosition() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// the empty paragraph starts at the second '\n', which is size 20
		RichText t = new RichTextBuilder(style(sans)).Append("a\n").Append("\nb", new TextStyleOverride(Size: 20f)).Build();
		Assert.Equal([10f, 20f, 20f], text.Layout(t).Lines.Select(static l => l.Height));
		Assert.Equal(20f, text.Layout(new RichText("", style(sans, 20f))).Height);
	}

	[Fact]
	public void SpansCanUseDifferentFonts() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		FontData digits = text.LoadFont(fontBytes("InjureTestDigits"));
		// no fallbacks needed: '1' is in the digits font, and the last span's fonts equal the first's
		RichText t = new RichTextBuilder(style(sans)).Append("a").Append("1", new TextStyleOverride(Fonts: digits)).Append("a", new TextStyleOverride(Color: SrgbColor32.Red)).Build();
		TextLayout l = text.Layout(t);
		Assert.Equal(16f, l.Width);
		Assert.Equal(2, l.Chains.Length);
	}

	[Fact]
	public void LineBreaksFollowEachCharactersLanguage() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// see TheLanguageSelectsLineBreakingRules; only the breaks inside "다라마" differ, so only its
		// language matters
		TextLayoutOptions box = TextLayoutOptions.Default with { MaxWidth = 18 };
		float[] widths(string outside, string inside) {
			RichText t = new RichTextBuilder(style(sans, language: outside))
				.Append("가나 ")
				.Append("다라마", new TextStyleOverride(Language: inside))
				.Build();
			return text.Layout(t, box).Lines.Select(static l => l.Width).ToArray();
		}
		Assert.Equal([10f, 15f], widths("ko", "ko-u-lw-keepall"));
		Assert.Equal([18f, 10f], widths("ko-u-lw-keepall", "ko"));
	}

	[Fact]
	public void GlyphsTakeTheirSpansColorTimesTheTint() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		RichText t = new RichTextBuilder(style(spec(sans)))
			.Append("a", new TextStyleOverride(Color: SrgbColor32.Red))
			.Append("b", new TextStyleOverride(Color: new SrgbColor32(0, 200, 100)))
			.Build();
		TextLayout layout = text.Layout(t);
		// 'a' covers pixel 2 and 'b' pixel 7, both fully
		byte[] px = render(16, 12, 1f, oc => oc.Canvas.Text(layout, Vector2.Zero));
		Assert.Equal(RawColor32.Red, GpuRig.At(px, 16, 2, 4));
		Assert.Equal(new RawColor32(0, 200, 100, 255), GpuRig.At(px, 16, 7, 4));
		// 200 * 128 / 255 = 100.39 and 100 * 128 / 255 = 50.20 round down; red's 0 channels stay 0
		px = render(16, 12, 1f, oc => oc.Canvas.Text(layout, Vector2.Zero, new SrgbColor32(128, 128, 128)));
		Assert.Equal(new RawColor32(128, 0, 0, 255), GpuRig.At(px, 16, 2, 4));
		Assert.Equal(new RawColor32(0, 100, 50, 255), GpuRig.At(px, 16, 7, 4));
	}

	[Fact]
	public void MissingGlyphsFallBackToLaterFonts() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		FontData digits = text.LoadFont(fontBytes("InjureTestDigits"));
		// without a fallback, '1' is .notdef
		Assert.Equal(10f, text.Layout("a1", style(sans)).Width);
		Assert.Equal(11f, text.Layout("a1", style(new FontFallbackChain(sans, digits))).Width);
	}

	[Fact]
	public void SpecsAndChainsCompareByContent() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		FontData digits = text.LoadFont(fontBytes("InjureTestDigits"));
		Assert.Equal(spec(sans), spec(sans));
		Assert.NotEqual(spec(sans), new FontSpec(sans));
		Assert.NotEqual(spec(sans), spec(digits));
		Assert.Equal(new FontFallbackChain(sans, digits), new FontFallbackChain(sans, digits));
		Assert.Equal(new FontFallbackChain(sans, digits).GetHashCode(), new FontFallbackChain(sans, digits).GetHashCode());
		Assert.NotEqual(new FontFallbackChain(sans, digits), new FontFallbackChain(digits, sans));
		Assert.True((FontFallbackChain)sans == new FontFallbackChain(sans));
	}

	[Fact]
	public void LayoutsDrawTheirGlyphs() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout layout = text.Layout("ab", style(spec(sans)));
		byte[] px = render(16, 12, 1f, oc => oc.Canvas.Text(layout, Vector2.Zero, SrgbColor32.White));
		// 'a' spans 0.5..4.5 px horizontally and 7 px up from the baseline at y = 8
		Assert.True(GpuRig.At(px, 16, 2, 4).R > 200);
		Assert.True(GpuRig.At(px, 16, 7, 4).R > 200);
		Assert.Equal(RawColor32.Black, GpuRig.At(px, 16, 12, 4));
		Assert.Equal(RawColor32.Black, GpuRig.At(px, 16, 2, 10));
	}

	[Fact]
	public void GlyphsAreRasterizedForTheEffectiveScale() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout layout = text.Layout("a", style(spec(sans)));
		// at 2 pixels per unit, the box covers exactly pixels 1..8 and rows 2..15; stretching a
		// 10px rasterization would blur its edges over neighboring pixels instead
		byte[] px = render(32, 24, 2f, oc => oc.Canvas.Text(layout, Vector2.Zero, SrgbColor32.White));
		Assert.Equal(255, GpuRig.At(px, 32, 1, 8).R);
		Assert.Equal(255, GpuRig.At(px, 32, 8, 8).R);
		Assert.Equal(0, GpuRig.At(px, 32, 0, 8).R);
		Assert.Equal(0, GpuRig.At(px, 32, 9, 8).R);
		Assert.Equal(255, GpuRig.At(px, 32, 4, 2).R);
		Assert.Equal(0, GpuRig.At(px, 32, 4, 1).R);
		Assert.Equal(255, GpuRig.At(px, 32, 4, 15).R);
		Assert.Equal(0, GpuRig.At(px, 32, 4, 16).R);
	}

	[Fact]
	public void GlyphsArePositionedToAQuarterPixel() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout layout = text.Layout("a", style(spec(sans)));
		// half a pixel to the right, the box covers exactly pixels 1..4, rather than being blurred
		// or snapped to a whole pixel
		byte[] px = render(16, 12, 1f, oc => oc.Canvas.Text(layout, new Vector2(0.5f, 0f), SrgbColor32.White));
		Assert.Equal(0, GpuRig.At(px, 16, 0, 4).R);
		Assert.Equal(255, GpuRig.At(px, 16, 1, 4).R);
		Assert.Equal(255, GpuRig.At(px, 16, 4, 4).R);
		Assert.Equal(0, GpuRig.At(px, 16, 5, 4).R);
	}

	[Fact]
	public void MonochromeRasterizationHasNoPartialCoverage() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout layout = text.Layout("ab", style(spec(sans, FontRasterMode.Monochrome)));
		byte[] px = render(16, 12, 1f, oc => oc.Canvas.Text(layout, new Vector2(0.25f, 0f), SrgbColor32.White));
		bool any = false;
		for (uint y = 0; y < 12; y++) {
			for (uint x = 0; x < 16; x++) {
				byte r = GpuRig.At(px, 16, x, y).R;
				Assert.True(r is 0 or 255, $"partial coverage {r} at ({x}, {y})");
				any |= r == 255;
			}
		}
		Assert.True(any);
	}

	[Fact]
	public void RotatedTextIsDrawnUnsnapped() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout layout = text.Layout("a", style(spec(sans)));
		// a quarter turn around (8, 8) maps the box (x 8.5..12.5, y 1..8) to x 8..15, y 8.5..12.5
		byte[] px = render(16, 16, 1f, oc => {
			Canvas cv = oc.Canvas;
			var rot = Matrix3x2.CreateRotation(MathF.PI / 2f, new Vector2(8f, 8f));
			using (cv.PushParams(transform: CanvasTransformOverride.Set(rot)))
				cv.Text(layout, new Vector2(8f, 0f), SrgbColor32.White);
		});
		Assert.True(GpuRig.At(px, 16, 11, 10).R > 200);
		Assert.Equal(0, GpuRig.At(px, 16, 11, 6).R);
		Assert.Equal(0, GpuRig.At(px, 16, 10, 3).R);
	}

	[Fact]
	public void AtlasPagesStayPinnedUntilTheFrameIsDone() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout layout = text.Layout("ab", style(spec(sans)));
		using GpuTexture target = GpuRig.Target(Device, 16, 12);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			var p = CanvasParams.Primary(Viewport.Whole(new Vector2(16, 12)), SrgbColor32.Black);
			using (OwnedCanvas canvas = new(frame, in p)) {
				canvas.Canvas.Text(layout, Vector2.Zero, SrgbColor32.White);
				canvas.Canvas.Text(layout, new Vector2(0f, 2f), SrgbColor32.White);
			}
			// pinned once per frame, however many draws use the page
			Assert.Equal(1, Assert.Single(text.Atlas.Pages).Refcount);
			frame.Submit();
		}
		Assert.Equal(0, Assert.Single(text.Atlas.Pages).Refcount);
	}

	[Fact]
	public void CanvasTextNeedsATextSystem() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		render(16, 12, 1f, oc => Assert.Throws<InvalidOperationException>(() => oc.Canvas.Text("a", style(sans), Vector2.Zero)));
		byte[] px = render(16, 12, 1f, oc => oc.Canvas.Text("a", style(spec(sans)), Vector2.Zero), text);
		Assert.True(GpuRig.At(px, 16, 2, 4).R > 200);
	}

	[Fact]
	public void CanvasTextCachesLayoutsWhileTheyreDrawn() {
		using TextSystem text = new(Device, cacheOptions: new TextCacheOptions { LayoutCacheFrames = 2 });
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		FontFallbackChain fonts = spec(sans);
		void nextFrame() {
			using RenderFrame f = new(Device, null);
			text.OnCanvasCreated(f);
		}

		nextFrame();
		TextLayoutOptions opts = TextLayoutOptions.Default;
		TextLayout a = text.GetCachedLayout("ab", style(fonts), opts);
		// equal inputs share a layout, different ones don't
		Assert.Same(a, text.GetCachedLayout("ab", style(new FontFallbackChain(spec(sans))), opts));
		Assert.NotSame(a, text.GetCachedLayout("ab", style(fonts, 20f), opts));
		Assert.NotSame(a, text.GetCachedLayout("ba", style(fonts), opts));
		Assert.NotSame(a, text.GetCachedLayout("ab", style(fonts), opts with { MaxWidth = 5 }));
		// so do equal rich texts, and a plain string and a single-span rich text are equal inputs
		RichText rich = new RichTextBuilder(style(fonts)).Append("a").Append("b", new TextStyleOverride(Color: SrgbColor32.Red)).Build();
		TextLayout r = text.GetCachedLayout(rich, opts);
		Assert.Same(r, text.GetCachedLayout(new RichTextBuilder(style(fonts)).Append("a").Append("b", new TextStyleOverride(Color: SrgbColor32.Red)).Build(), opts));
		Assert.NotSame(a, r);
		Assert.Same(a, text.GetCachedLayout(new RichText("ab", style(fonts)), opts));
		nextFrame();
		nextFrame();
		Assert.Same(a, text.GetCachedLayout("ab", style(fonts), opts));
		nextFrame();
		nextFrame();
		nextFrame();
		Assert.NotSame(a, text.GetCachedLayout("ab", style(fonts), opts));
	}

	private sealed class FontStore : IDisposable {
		public readonly AssetStore Store = new();
		public readonly MemorySource Source = new();
		public readonly TextSystem Text;
		public readonly Asset<FontData> Font;

		public FontStore(GpuDevice device, string initial) {
			Text = new TextSystem(device);
			Store.RegisterSource(new OwnerOrderedEntry<IAssetSource>(Source, "test", "memory"));
			BuiltinAssetRegistrations.RegisterFontInto(Store, Text);
			AssetId id = new("test", "font.ttf");
			Source.Set(id, fontBytes(initial));
			Font = Store.GetAsset<FontData>(id);
		}

		public void Reload(string name) {
			Source.Set(Font.AssetId, fontBytes(name));
			Font.QueueReload();
			Store.ApplyQueuedReloadsOrThrow();
		}

		public void Dispose() {
			Store.Dispose();
			Text.Dispose();
		}
	}

	[Fact]
	public void LayoutsOfReloadedFontsGoStaleAndRelayout() {
		using FontStore fs = new(Device, "InjureTestSans");
		using AssetThreadAttachment att = fs.Store.AttachCurrentThread();
		TextLayout layout = fs.Text.Layout("aaa", style(fs.Font));
		Assert.Equal(15f, layout.Width);
		Assert.False(layout.IsStale);
		Assert.False(layout.RelayoutIfStale());

		fs.Reload("InjureTestSansWide");
		Assert.True(layout.IsStale);
		// the metrics describe the old layout until it's laid out again
		Assert.Equal(15f, layout.Width);
		Assert.True(layout.RelayoutIfStale());
		Assert.False(layout.IsStale);
		// em fractions like 0.7 aren't exact in floats
		Assert.Equal(21f, layout.Width, 4);
		Assert.Equal(14f, fs.Text.Layout("aa", style(fs.Font)).Width, 4);

		// drawing a stale layout lays it out again, after the old states were reclaimed too
		fs.Reload("InjureTestSans");
		att.AtReloadBoundary();
		fs.Store.ApplyQueuedReloads();
		Assert.True(layout.IsStale);
		render(32, 12, 1f, oc => oc.Canvas.Text(layout, Vector2.Zero, SrgbColor32.White));
		Assert.False(layout.IsStale);
		Assert.Equal(15f, layout.Width);
	}

	[Fact]
	public void FontStatesOutliveTheirTextSystem() {
		FontStore fs = new(Device, "InjureTestSans");
		using (AssetThreadAttachment att = fs.Store.AttachCurrentThread())
			Assert.Equal(10f, fs.Text.Layout("aa", style(fs.Font)).Width);
		// the store reclaims the states after the text system is gone, which they survive by
		// keeping FreeType alive
		fs.Text.Dispose();
		fs.Store.Dispose();
	}

	[Fact]
	public void LayoutsOfDisposedSystemsThrow() {
		TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayout layout = text.Layout("a", style(sans));
		text.Dispose();
		Assert.Throws<ObjectDisposedException>(() => layout.IsStale);
		render(16, 12, 1f, oc => Assert.Throws<ObjectDisposedException>(() => oc.Canvas.Text(layout, Vector2.Zero, SrgbColor32.White)));
	}
}
