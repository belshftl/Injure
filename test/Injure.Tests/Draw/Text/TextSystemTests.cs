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

// the fonts come from Fonts/make_test_fonts.py: every glyph is a box spanning 50 units in from
// either side of its advance and 700 units up from the baseline, letters advance 500 units ('m'
// 1000), the wide variant's letters 700, digits 600, space 300, .notdef 500, with an ascent of 800,
// a descent of 200, and 1000 units per em, so at size 10 everything is whole drawable units
public sealed class TextSystemTests : GpuTestBase {
	// layout uses libunibreak through ijmisc; the libmisc collection can't be used here, since this
	// class is already in the GPU one
	public TextSystemTests(GpuDeviceFixture fixture) : base(fixture) => LibmiscNativeLoader.Init();

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

	private static TextLine[] aligned(TextSystem text, FontData sans, string s, float maxWidth, TextHorizontalAlign align) =>
		text.Layout(s, style(sans), TextLayoutOptions.Default with { MaxWidth = maxWidth, WrapMode = TextWrapMode.None, HorizontalAlign = align }).Lines.ToArray();

	[Fact]
	public void TheLanguageSelectsLineBreakingRules() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// the font has no guillemets, so they're 500-unit .notdefs, as wide as 'a'
		// "« b »" is exactly as wide as the box
		// french doesn't break inside guillemets, so it keeps "« b »" together, while the default rules
		// break between 'b' and '»'
		const string s = "a \u00ab b \u00bb c";
		TextLayoutOptions box = TextLayoutOptions.Default with { MaxWidth = 21 };
		float[] widths(string language) =>
			text.Layout(s, style(sans, language: language), box).Lines.Select(static l => l.Width).ToArray();
		Assert.Equal([21f, 13f], widths("und"));
		Assert.Equal([21f, 13f], widths("en"));
		Assert.Equal([5f, 21f, 5f], widths("fr"));
		Assert.Equal([5f, 21f, 5f], widths("fr-CA"));
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
	public void UnpairedSurrogatesAreReplacedWithReplacementCharacters() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// the test font has no U+FFFD, so it's a 500-unit .notdef
		TextLayout l = text.Layout("a\ud800b", style(sans));
		Assert.Equal(15f, l.Width);
		Assert.Equal("a\ufffdb", l.Text.Text);
		TextLayout lone = text.Layout("\udc00", style(sans));
		Assert.Equal("\ufffd", lone.Text.Text);
		Assert.Equal(5f, lone.Width);
		// valid text is kept as is, without a copy
		string valid = "ab\ud83d\ude00";
		Assert.Same(valid, text.Layout(valid, style(sans)).Text.Text);
		// the layout cache still finds the layout by the text as given
		TextLayout cached = text.GetCachedLayout("a\ud800b", style(sans), TextLayoutOptions.Default);
		Assert.Equal("a\ufffdb", cached.Text.Text);
		Assert.Same(cached, text.GetCachedLayout("a\ud800b", style(sans), TextLayoutOptions.Default));
	}

	[Fact]
	public void RightToLeftTextIsLaidOutInVisualOrder() {
		using TextSystem text = new(Device);
		FontData sans = text.LoadFont(fontBytes("InjureTestSans"));
		// the glyphs are all .notdefs since the font doesn't have hebrew, tell apart by source indices
		static int[] visualOrder(TextLayout l) =>
			l.Glyphs.ToArray().OrderBy(static g => g.X).Select(static g => g.SourceIndex).ToArray();
		// an LTR paragraph with an RTL run: the run's characters go right to left
		Assert.Equal([0, 1, 2, 4, 3], visualOrder(text.Layout("ab \u05d0\u05d1", style(sans))));
		// an RTL paragraph with an LTR run: the paragraph goes right to left, the run left to right
		Assert.Equal([3, 4, 2, 1, 0], visualOrder(text.Layout("\u05d0\u05d1 ab", style(sans))));
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
		// see TheLanguageSelectsLineBreakingRules; only the breaks inside the guillemets differ, so
		// only the language of what's inside them matters
		TextLayoutOptions box = TextLayoutOptions.Default with { MaxWidth = 21 };
		float[] widths(string outside, string inside) {
			RichText t = new RichTextBuilder(style(sans, language: outside))
				.Append("a ")
				.Append("\u00ab b \u00bb", new TextStyleOverride(Language: inside))
				.Append(" c")
				.Build();
			return text.Layout(t, box).Lines.Select(static l => l.Width).ToArray();
		}
		Assert.Equal([5f, 21f, 5f], widths("und", "fr"));
		Assert.Equal([21f, 13f], widths("fr", "und"));
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
