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

// the fonts come from Fonts/make_test_fonts.py: every glyph is a box, letters advance 500 units
// ('m' 1000), the wide variant's letters 700, digits 600, space 300, .notdef 500, with an ascent
// of 800 and a descent of 200, so at 10px everything is whole pixels
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

	// unhinted, so advances are exactly the font's; hinting would snap the box edges and widen them
	private static TextStyle style(TextLayoutOptions? layout = null) =>
		new(new FontOptions(10, Hinting: FontHinting.None), SrgbColor32.White, layout ?? new TextLayoutOptions());

	[Fact]
	public void LoadFontRejectsNonFontsAndDisposedSystems() {
		TextSystem text = new(Device);
		Assert.Throws<FTException>(() => text.LoadFont([1, 2, 3, 4, 5, 6, 7, 8]));
		// font asset creators call this from loading threads
		Exception? caught = null;
		Font? loaded = null;
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
	public void MeasuringUsesAdvancesAndLineMetrics() {
		using TextSystem text = new(Device);
		Font sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextMeasurement m = text.Measure(sans, "aaa", style());
		Assert.Equal(15f, m.Width);
		Assert.Equal(10f, m.Height);
		Assert.Equal(1, m.LineCount);
		Assert.Equal(15f, text.Measure(sans, "am", style()).Width);
		Assert.Equal(13f, text.Measure(sans, "a a", style()).Width);
	}

	[Fact]
	public void GreedyWrappingBreaksAtSpaces() {
		using TextSystem text = new(Device);
		Font sans = text.LoadFont(fontBytes("InjureTestSans"));
		TextLayoutMeasurement m = text.MeasureLayout(sans, "aa aa aa", style(new TextLayoutOptions(MaxWidth: 26, WrapMode: TextWrapMode.Greedy)));
		Assert.Equal(2, m.LineCount);
		// trailing spaces don't count towards a line's width
		Assert.Equal(23f, m.Lines[0].Width);
		Assert.Equal(10f, m.Lines[1].Width);
		Assert.Equal(20f, m.Height);
		Assert.Equal(1, text.Measure(sans, "aa aa aa", style()).LineCount);
	}

	[Fact]
	public void MissingGlyphsFallBackToLaterFonts() {
		using TextSystem text = new(Device);
		Font sans = text.LoadFont(fontBytes("InjureTestSans"));
		Font digits = text.LoadFont(fontBytes("InjureTestDigits"));
		// without a fallback, '1' is .notdef
		Assert.Equal(10f, text.Measure(sans, "a1", style()).Width);
		Assert.Equal(11f, text.Measure(new FontFallbackChain(sans, digits), "a1", style()).Width);
	}

	[Fact]
	public void LayoutsRenderTheirGlyphs() {
		using TextSystem text = new(Device);
		Font sans = text.LoadFont(fontBytes("InjureTestSans"));
		using TextLayout layout = text.Layout(sans, "ab", style());
		Assert.Equal(10f, layout.Width);
		Assert.Equal(2, layout.Glyphs.Length);

		using GpuTexture target = GpuRig.Target(Device, 16, 12);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			using (OwnedCanvas canvas = new(frame, CanvasParams.Primary(Matrix3x2.Identity, SrgbColor32.Black)))
				layout.Render(canvas.Canvas, Vector2.Zero);
			frame.Submit();
		}
		byte[] px = GpuRig.Read(Device, target);
		// a box spans 0.5..4.5 px horizontally and 7 px up from the baseline at y = 8
		Assert.True(GpuRig.At(px, 16, 2, 4).R > 200);
		Assert.True(GpuRig.At(px, 16, 7, 4).R > 200);
		Assert.Equal(RawColor32.Black, GpuRig.At(px, 16, 12, 4));
		Assert.Equal(RawColor32.Black, GpuRig.At(px, 16, 2, 10));
	}

	private sealed class FontStore : IDisposable {
		public readonly AssetStore Store = new();
		public readonly MemorySource Source = new();
		public readonly TextSystem Text;
		public readonly Asset<Font> Font;

		public FontStore(GpuDevice device, string initial) {
			Text = new TextSystem(device);
			Store.RegisterSource(new OwnerOrderedEntry<IAssetSource>(Source, "test", "memory"));
			BuiltinAssetRegistrations.RegisterFontInto(Store, Text);
			AssetId id = new("test", "font.ttf");
			Source.Set(id, fontBytes(initial));
			Font = Store.GetAsset<Font>(id);
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
	public void AssetFontsFollowReloads() {
		using FontStore fs = new(Device, "InjureTestSans");
		using AssetThreadAttachment att = fs.Store.AttachCurrentThread();
		Assert.Equal(10f, fs.Text.Measure(fs.Font, "aa", style()).Width);
		using LiveText live = fs.Text.Make(fs.Font, "aaa", style());
		Assert.Equal(15f, live.Layout.Width);

		fs.Reload("InjureTestSansWide");
		Assert.Equal(14f, fs.Text.Measure(fs.Font, "aa", style()).Width);
		Assert.Equal(21f, live.Layout.Width);

		// the old faces and states get reclaimed without disturbing the new ones
		att.AtReloadBoundary();
		fs.Store.ApplyQueuedReloads();
		Assert.Equal(14f, fs.Text.Measure(fs.Font, "aa", style()).Width);
		fs.Reload("InjureTestSans");
		att.AtReloadBoundary();
		fs.Store.ApplyQueuedReloads();
		Assert.Equal(10f, fs.Text.Measure(fs.Font, "aa", style()).Width);
	}

	[Fact]
	public void FontStatesOutliveTheirTextSystem() {
		FontStore fs = new(Device, "InjureTestSans");
		using (AssetThreadAttachment att = fs.Store.AttachCurrentThread())
			Assert.Equal(10f, fs.Text.Measure(fs.Font, "aa", style()).Width);
		// the store reclaims the states after the text system is gone, which they survive by
		// keeping FreeType alive
		fs.Text.Dispose();
		fs.Store.Dispose();
	}
}
