// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Draw.PixelConv;
using Injure.Gpu;
using Injure.Primitives;
using Injure.Tests.Gpu;

namespace Injure.Tests.Draw;

public sealed class CanvasTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private static readonly RawColor32 red = RawColor32.Red;
	private static readonly RawColor32 green = RawColor32.Green;
	private static readonly RawColor32 blue = RawColor32.Blue;
	private static readonly RawColor32 black = RawColor32.Black;

	private static CanvasParams baseParams(RawColor32 clear) => CanvasParams.Primary(clear.AssumeSrgb());

	// draws into a 4x4 test texture standing in for the primary output and returns its texels
	private byte[] render(RawColor32 clear, Action<OwnedCanvas> draw) {
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			using (OwnedCanvas canvas = new(frame, baseParams(clear)))
				draw(canvas);
			frame.Submit();
		}
		return GpuRig.Read(Device, target);
	}

	private Texture2d solid(RawColor32 color) {
		Texture2d tex = new(Device, 1, 1);
		tex.Upload([color.R, color.G, color.B, color.A], 4, PixelFormat.Rgba32_Unorm);
		return tex;
	}

	// regression test: render targets used to get the primary target's projection
	[Fact]
	public void RenderTargetsOfOtherSizesUseTheirOwnProjection() {
		using RenderTarget2d rt = new(Device, 8, 8);
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(target: rt, colorAttachmentOps: ColorAttachmentOps.Clear(blue.ToRawF128())))
				cv.Rect(new RectF(0, 0, 4, 8), red.AssumeSrgb());
			// the 8x8 target scaled onto the 4x4 primary: left half red, right half blue
			cv.Texture(rt, new RectF(0, 0, 4, 4));
		});
		Assert.Equal(red, GpuRig.At(px, 4, 0, 1));
		Assert.Equal(red, GpuRig.At(px, 4, 1, 2));
		Assert.Equal(blue, GpuRig.At(px, 4, 2, 1));
		Assert.Equal(blue, GpuRig.At(px, 4, 3, 2));
	}

	[Fact]
	public void InterleavedPrimitivesAndTexturesKeepCallOrder() {
		using Texture2d greenTex = solid(green);
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			cv.Rect(new RectF(0, 0, 4, 4), red.AssumeSrgb());
			cv.Texture(greenTex, new RectF(0, 0, 2, 4));
			cv.Rect(new RectF(0, 0, 1, 1), blue.AssumeSrgb());
		});
		Assert.Equal(blue, GpuRig.At(px, 4, 0, 0));
		Assert.Equal(green, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(green, GpuRig.At(px, 4, 0, 3));
		Assert.Equal(red, GpuRig.At(px, 4, 3, 3));
	}

	[Fact]
	public void TextureDrawsHonorRegionDestinationAndTint() {
		// 2x1 texture: red, green
		using Texture2d tex = new(Device, 2, 1);
		tex.Upload([255, 0, 0, 255, 0, 255, 0, 255], 8, PixelFormat.Rgba32_Unorm);
		using Texture2d white = solid(RawColor32.White);
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			// pixel region at a position: natural size of the region, i.e. 1x1
			cv.Texture(tex, new TexDraw(new Vector2(0, 0)) { Source = TexRegion.Pixels(new RectF(1, 0, 1, 1)) });
			// uv region stretched into a rectangle
			cv.Texture(tex, new TexDraw(new RectF(2, 0, 2, 4)) { Source = TexRegion.Uv(new RectF(0, 0, 0.5f, 1)) });
			cv.Texture(white, new TexDraw(new RectF(0, 2, 2, 2)) { Tint = blue.AssumeSrgb() });
			bool threw = false;
			try {
				cv.Texture(tex, default);
			} catch (ArgumentException) {
				threw = true;
			}
			Assert.True(threw);
		});
		Assert.Equal(green, GpuRig.At(px, 4, 0, 0));
		Assert.Equal(black, GpuRig.At(px, 4, 1, 0));
		Assert.Equal(red, GpuRig.At(px, 4, 3, 3));
		Assert.Equal(blue, GpuRig.At(px, 4, 1, 3));
	}

	[Fact]
	public void TextureDrawsRotateScaleAndFlipAroundTheOrigin() {
		// 2x1 texture: red, green; drawn into the whole 4x4 target it's left half red, right half green
		using Texture2d tex = new(Device, 2, 1);
		tex.Upload([255, 0, 0, 255, 0, 255, 0, 255], 8, PixelFormat.Rgba32_Unorm);
		using Texture2d white = solid(RawColor32.White);
		RectF full = new(0, 0, 4, 4);

		// a quarter turn clockwise around the center moves the left half to the top
		byte[] px = render(black, canvas => canvas.Canvas.Texture(tex, new TexDraw(full) { Origin = new Vector2(0.5f), Rotation = MathF.PI / 2f }));
		Assert.Equal(red, GpuRig.At(px, 4, 1, 0));
		Assert.Equal(green, GpuRig.At(px, 4, 2, 3));

		px = render(black, canvas => canvas.Canvas.Texture(tex, new TexDraw(full) { FlipX = true }));
		Assert.Equal(green, GpuRig.At(px, 4, 0, 2));
		Assert.Equal(red, GpuRig.At(px, 4, 3, 2));

		// anchored at its bottom-right corner, scaled from 1x1 to 2x2: covers [2, 4) on both axes
		px = render(black, canvas => canvas.Canvas.Texture(white, new TexDraw(new Vector2(4, 4)) { Origin = Vector2.One, Scale = new Vector2(2) }));
		Assert.Equal(RawColor32.White, GpuRig.At(px, 4, 2, 2));
		Assert.Equal(RawColor32.White, GpuRig.At(px, 4, 3, 3));
		Assert.Equal(black, GpuRig.At(px, 4, 1, 1));

		render(black, canvas => {
			bool threw = false;
			try {
				canvas.Canvas.Texture(white, new TexDraw(full) { Rotation = float.NaN });
			} catch (ArgumentException) {
				threw = true;
			}
			Assert.True(threw);
		});
	}

	// 2x1 texture: red, blue
	private Texture2d redBlue() {
		Texture2d tex = new(Device, 2, 1);
		tex.Upload([255, 0, 0, 255, 0, 0, 255, 255], 8, PixelFormat.Rgba32_Unorm);
		return tex;
	}

	// the 2x1 red/blue texture stretched across a 4-pixel row samples at texel x -0.25, 0.25, 0.75,
	// and 1.25, so nearest gives red, red, blue, blue, and linear blends the middle two
	private static void assertRow(byte[] px, uint y, bool linear) {
		Assert.Equal(red, GpuRig.At(px, 4, 0, y));
		Assert.Equal(blue, GpuRig.At(px, 4, 3, y));
		if (linear) {
			RawColor32 a = GpuRig.At(px, 4, 1, y), b = GpuRig.At(px, 4, 2, y);
			Assert.True(a.R > a.B && a.B > 0, $"expected a mostly red blend at (1, {y}), got {a}");
			Assert.True(b.B > b.R && b.R > 0, $"expected a mostly blue blend at (2, {y}), got {b}");
		} else {
			Assert.Equal(red, GpuRig.At(px, 4, 1, y));
			Assert.Equal(blue, GpuRig.At(px, 4, 2, y));
		}
	}

	[Fact]
	public void SamplingComesFromTheDrawOrTheCanvas() {
		using Texture2d tex = redBlue();
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			cv.Texture(tex, new RectF(0, 0, 4, 1));
			using (cv.PushParams(sampling: TexSampling.Linear)) {
				cv.Texture(tex, new RectF(0, 1, 4, 1));
				cv.Texture(tex, new TexDraw(new RectF(0, 2, 4, 1)) { Sampling = TexSampling.Nearest });
			}
			cv.Texture(tex, new TexDraw(new RectF(0, 3, 4, 1)) { Sampling = TexSampling.Linear });
		});
		assertRow(px, 0, linear: false);
		assertRow(px, 1, linear: true);
		assertRow(px, 2, linear: false);
		assertRow(px, 3, linear: true);
	}

	[Fact]
	public void UntexturedDrawsDontDependOnSampling() {
		using Texture2d tex = redBlue();
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			cv.Texture(tex, new TexDraw(new RectF(0, 0, 4, 1)) { Sampling = TexSampling.Linear });
			cv.Rect(new RectF(0, 1, 4, 1), green.AssumeSrgb());
			cv.Texture(tex, new RectF(0, 2, 4, 1));
			cv.Rect(new RectF(0, 3, 4, 1), green.AssumeSrgb());
		});
		assertRow(px, 0, linear: true);
		assertRow(px, 2, linear: false);
		for (uint x = 0; x < 4; x++) {
			Assert.Equal(green, GpuRig.At(px, 4, x, 1));
			Assert.Equal(green, GpuRig.At(px, 4, x, 3));
		}
	}

	[Fact]
	public void WrapModesHandleUvsOutsideTheTexture() {
		// u from 0 to 2 across 4 pixels samples texel centers 0, 1, 2, 3
		static TexDraw row(int y, TexWrap wrap) => new(new RectF(0, y, 4, 1)) {
			Source = TexRegion.Uv(new RectF(0, 0, 2, 1)),
			Sampling = new TexSampling(TexFilter.Nearest, wrap),
		};

		using Texture2d tex = redBlue();
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			cv.Texture(tex, row(0, TexWrap.Clamp));
			cv.Texture(tex, row(1, TexWrap.Repeat));
			cv.Texture(tex, row(2, TexWrap.MirrorRepeat));
		});
		Assert.Equal([red, blue, blue, blue], Enumerable.Range(0, 4).Select(x => GpuRig.At(px, 4, (uint)x, 0)));
		Assert.Equal([red, blue, red, blue], Enumerable.Range(0, 4).Select(x => GpuRig.At(px, 4, (uint)x, 1)));
		Assert.Equal([red, blue, blue, red], Enumerable.Range(0, 4).Select(x => GpuRig.At(px, 4, (uint)x, 2)));
	}

	[Fact]
	public void TransformsApplyPerDraw() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(transform: Matrix3x2.CreateTranslation(2, 0)))
				cv.Rect(new RectF(0, 0, 2, 2), red.AssumeSrgb());
			cv.Rect(new RectF(0, 2, 2, 2), blue.AssumeSrgb());
		});
		Assert.Equal(black, GpuRig.At(px, 4, 0, 0));
		Assert.Equal(red, GpuRig.At(px, 4, 3, 1));
		Assert.Equal(blue, GpuRig.At(px, 4, 1, 3));
		Assert.Equal(black, GpuRig.At(px, 4, 3, 3));
	}

	[Fact]
	public void ScissorClipsAndRestores() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(scissor: CanvasScissor.Set(new RectI(0, 0, 2, 2))))
				cv.Rect(new RectF(0, 0, 4, 4), red.AssumeSrgb());
			cv.Rect(new RectF(0, 3, 4, 1), blue.AssumeSrgb());
		});
		Assert.Equal(red, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 3, 0));
		Assert.Equal(blue, GpuRig.At(px, 4, 3, 3));
	}

	// 4x4 checkerboard of a and b
	private Texture2d checker(RawColor32 a, RawColor32 b, Texture2dMipFilter? mips) {
		Texture2d tex = new(Device, new Texture2dCreateParams(4, 4, Mips: mips));
		byte[] px = new byte[4 * 4 * 4];
		for (int y = 0; y < 4; y++)
			for (int x = 0; x < 4; x++)
				((x + y) % 2 == 0 ? a : b).WriteRgbaBytes(px.AsSpan((y * 4 + x) * 4));
		tex.Upload(px, 16, PixelFormat.Rgba32_Unorm);
		return tex;
	}

	// a 4x4 texture drawn into one pixel samples its 1x1 mip level; opaque output writes the sampled
	// value without blending it with the target
	private RawColor32 sampleSmallest(Texture2d tex) {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(outputState: CanvasOutputStates.Opaque, sampling: TexSampling.Linear))
				cv.Texture(tex, new RectF(0, 0, 1, 1));
		});
		return GpuRig.At(px, 4, 0, 0);
	}

	private static void assertNear(RawColor32 expected, RawColor32 actual) {
		const int tolerance = 2;
		Assert.True(
			Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
				&& Math.Abs(expected.B - actual.B) <= tolerance && Math.Abs(expected.A - actual.A) <= tolerance,
			$"expected about {expected}, got {actual}"
		);
	}

	[Fact]
	public void TexturesHaveFullMipChainsWhenAskedTo() {
		using Texture2d oneByOne = new(Device, new Texture2dCreateParams(1, 1, Mips: Texture2dMipFilter.Raw));
		using Texture2d wide = new(Device, new Texture2dCreateParams(5, 3, Mips: Texture2dMipFilter.Raw));
		using Texture2d big = new(Device, new Texture2dCreateParams(256, 64, Mips: Texture2dMipFilter.Color));
		Assert.Equal(1u, oneByOne.MipLevelCount);
		Assert.Equal(3u, wide.MipLevelCount);
		Assert.Equal(9u, big.MipLevelCount);
		Assert.Equal(Texture2dMipFilter.Color, big.MipFilter);

		using Texture2d single = new(Device, 5, 3);
		Assert.Equal(1u, single.MipLevelCount);
		Assert.Null(single.MipFilter);
		Assert.Throws<InvalidOperationException>(single.GenerateMips);
		Assert.Throws<ArgumentException>(() => new Texture2d(Device, new Texture2dCreateParams(4, 4, Mips: default(Texture2dMipFilter))));
		Assert.Throws<ArgumentException>(() => new Texture2d(Device, new Texture2dCreateParams(4, 4, Format: default(Texture2dFormat))));
	}

	[Fact]
	public void ColorMipsAverageInLinearLight() {
		using Texture2d color = checker(red, blue, Texture2dMipFilter.Color);
		using Texture2d raw = checker(red, blue, Texture2dMipFilter.Raw);
		// half of 1.0 in linear light, sRGB-encoded, is 188
		assertNear(new RawColor32(188, 0, 188), sampleSmallest(color));
		assertNear(new RawColor32(128, 0, 128), sampleSmallest(raw));
	}

	[Fact]
	public void ColorMipsDontBleedTransparentTexels() {
		RawColor32 clearGreen = new(0, 255, 0, 0);
		using Texture2d color = checker(red, clearGreen, Texture2dMipFilter.Color);
		using Texture2d raw = checker(red, clearGreen, Texture2dMipFilter.Raw);
		assertNear(new RawColor32(255, 0, 0, 128), sampleSmallest(color));
		assertNear(new RawColor32(128, 128, 0, 128), sampleSmallest(raw));
	}

	[Fact]
	public void MipsAreRegeneratedAfterUploads() {
		using Texture2d tex = checker(red, red, Texture2dMipFilter.Raw);
		assertNear(red, sampleSmallest(tex));
		// a partial upload covering a quarter of the texture
		byte[] bluePx = new byte[2 * 2 * 4];
		for (int i = 0; i < 4; i++)
			blue.WriteRgbaBytes(bluePx.AsSpan(i * 4));
		tex.Upload(0, 0, bluePx, 8, PixelFormat.Rgba32_Unorm, 2, 2);
		assertNear(new RawColor32(191, 0, 64), sampleSmallest(tex));
		tex.Upload(2, 2, bluePx, 8, PixelFormat.Rgba32_Unorm, 2, 2);
		tex.GenerateMips();
		tex.GenerateMips();
		assertNear(new RawColor32(128, 0, 128), sampleSmallest(tex));
	}

	// 2x2 texture: red, green / blue, white
	private Texture2d quadrants() {
		Texture2d tex = new(Device, 2, 2);
		tex.Upload([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255], 8, PixelFormat.Rgba32_Unorm);
		return tex;
	}

	[Fact]
	public void SlicesDrawLikeTexturesOfTheirOwn() {
		using Texture2d tex = quadrants();
		Texture2dSlice[] cells = Texture2dSlice.Grid(tex, new Vector2Int(1, 1));
		Texture2dSlice topRow = new(tex, new RectI(0, 0, 2, 1));
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			// natural size of a slice is its own size
			cv.Texture(cells[1], new Vector2(0, 0));
			cv.Texture(cells[3], new RectF(2, 0, 2, 2));
			// regions are relative to the slice
			cv.Texture(topRow, new TexDraw(new Vector2(1, 0)) { Source = TexRegion.Pixels(new RectF(1, 0, 1, 1)) });
			cv.Texture(topRow, new TexDraw(new RectF(0, 2, 2, 2)) { Source = TexRegion.Uv(new RectF(0, 0, 0.5f, 1)) });
			cv.Texture(cells[2], new TexDraw(new Vector2(0, 1)) { FlipX = true });
		});
		Assert.Equal(green, GpuRig.At(px, 4, 0, 0));
		Assert.Equal(green, GpuRig.At(px, 4, 1, 0));
		Assert.Equal(RawColor32.White, GpuRig.At(px, 4, 3, 1));
		Assert.Equal(blue, GpuRig.At(px, 4, 0, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(red, GpuRig.At(px, 4, 1, 3));
	}

	[Fact]
	public void SlicesOfOneTextureShareADrawCall() {
		using Texture2d tex = quadrants();
		using Texture2d other = solid(green);
		Texture2dSlice[] cells = Texture2dSlice.Grid(tex, new Vector2Int(1, 1));
		OwnedCanvas? oc = null;
		render(black, canvas => {
			oc = canvas;
			Canvas cv = canvas.Canvas;
			for (int i = 0; i < 16; i++)
				cv.Texture(cells[i % 4], new Vector2(i % 4, i / 4));
		});
		Assert.Equal(1, oc!.DrawCallCount);
		render(black, canvas => {
			oc = canvas;
			Canvas cv = canvas.Canvas;
			cv.Texture(cells[0], new Vector2(0, 0));
			cv.Texture(other, new Vector2(1, 0));
			cv.Texture(cells[1], new Vector2(2, 0));
		});
		Assert.Equal(3, oc!.DrawCallCount);
	}

	[Fact]
	public void SlicesRejectRectsAndRegionsOutsideThem() {
		using Texture2d tex = quadrants();
		Assert.Throws<ArgumentException>(() => new Texture2dSlice(tex, new RectI(1, 1, 2, 1)));
		Assert.Throws<ArgumentException>(() => new Texture2dSlice(tex, new RectI(-1, 0, 1, 1)));
		Assert.Throws<ArgumentException>(() => new Texture2dSlice(tex, new RectI(0, 0, 0, 1)));
		Assert.Throws<ArgumentInvalidDefaultException>(() => new Texture2dSlice(default, new RectI(0, 0, 1, 1)));
		Assert.Throws<ArgumentInvalidDefaultException>(() => (DrawableTexture)default(Texture2dSlice));
		Texture2dSlice topRow = new(tex, new RectI(0, 0, 2, 1));
		Assert.Throws<ArgumentException>(() => topRow.Slice(new RectI(0, 0, 1, 2)));
		Assert.Equal(new RectI(1, 0, 1, 1), topRow.Slice(new RectI(1, 0, 1, 1)).Rect);
		Assert.Equal(new Texture2dSlice(tex, new RectI(1, 0, 1, 1)), topRow.Slice(new RectI(1, 0, 1, 1)));

		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			Texture2dSlice cell = new(tex, new RectI(0, 0, 1, 1));
			TexDraw[] bad = [
				new(new Vector2(0, 0)) { Source = TexRegion.Pixels(new RectF(1, 0, 1, 1)) },
				new(new Vector2(0, 0)) { Source = TexRegion.Uv(new RectF(0, 0, 2, 1)) },
			];
			foreach (TexDraw d in bad) {
				bool threw = false;
				try {
					cv.Texture(cell, d);
				} catch (ArgumentException) {
					threw = true;
				}
				Assert.True(threw);
			}
			cv.Texture(cell, new TexDraw(new Vector2(0, 0)) { Source = TexRegion.Uv(new RectF(0, 0, 1, 1)) });
		});
	}

	[Fact]
	public void GridsHonorMarginAndSpacing() {
		using Texture2d tex = new(Device, 7, 4);
		// with margin 1 and spacing 1, 2x2 cells fit twice horizontally and once vertically
		Texture2dSlice[] cells = Texture2dSlice.Grid(tex, new Vector2Int(2, 2), margin: 1, spacing: 1);
		Assert.Equal([new RectI(1, 1, 2, 2), new RectI(4, 1, 2, 2)], cells.Select(c => c.Rect));
		Assert.Throws<ArgumentException>(() => Texture2dSlice.Grid(tex, new Vector2Int(8, 1)));
		Assert.Throws<ArgumentException>(() => Texture2dSlice.Grid(tex, new Vector2Int(2, 2), 3, 1));

		// grids of a slice are relative to it, ordered row by row
		Texture2dSlice right = new(tex, new RectI(3, 0, 4, 4));
		Texture2dSlice[] sub = Texture2dSlice.Grid(right, new Vector2Int(2, 2), 2, 2);
		Assert.Equal(
			[new RectI(3, 0, 2, 2), new RectI(5, 0, 2, 2), new RectI(3, 2, 2, 2), new RectI(5, 2, 2, 2)],
			sub.Select(c => c.Rect)
		);
		Assert.All(sub, c => Assert.Equal((DrawableTexture)tex, c.Texture));
	}

	[Fact]
	public void StoreOwnedTexturesCantBeDisposedUntilReleased() {
		Texture2d tex = new(Device, 1, 1);
		Assert.False(tex.IsAssetStoreOwned);
		Injure.Assets.AssetStoreCustody custody = new(new Injure.Assets.AssetId("test", "tex.png"), "version 3 of asset 'test::tex.png'", 3);
		((Injure.Assets.IAssetStoreValue)tex).AttachCustody(custody);
		Assert.True(tex.IsAssetStoreOwned);
		Assert.Throws<InvalidOperationException>(tex.Dispose);
		custody.Release();
		tex.Dispose();
		ObjectDisposedException ex = Assert.Throws<ObjectDisposedException>(() => tex.Width);
		Assert.Contains("test::tex.png", ex.Message);
	}

	[Fact]
	public void ClearReplacesTheWholeTargetIgnoringTransformAndBlending() {
		RawColor32 translucent = new(0, 0, 255, 128);
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			cv.Rect(new RectF(0, 0, 4, 4), red.AssumeSrgb());
			using (cv.PushParams(transform: Matrix3x2.CreateScale(0.25f)))
				cv.Clear(translucent.AssumeSrgb());
			cv.Rect(new RectF(3, 3, 1, 1), green.AssumeSrgb());
		});
		Assert.Equal(translucent, GpuRig.At(px, 4, 0, 0));
		Assert.Equal(translucent, GpuRig.At(px, 4, 3, 0));
		Assert.Equal(green, GpuRig.At(px, 4, 3, 3));
	}

	[Fact]
	public void ClearRespectsTheScissor() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(scissor: CanvasScissor.Set(new RectI(0, 0, 2, 2))))
				cv.Clear(red.AssumeSrgb());
		});
		Assert.Equal(red, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 2, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 1, 2));
	}

	[Fact]
	public void PrimaryFactoryFillsInTheCommonParams() {
		var p = CanvasParams.Primary(SrgbColor32.Red);
		Assert.Equal(new CanvasParams(
			Target: CanvasTarget.Primary,
			ColorAttachmentOps: ColorAttachmentOps.Clear(SrgbColor32.Red.ToRawF128()),
			Scissor: CanvasScissor.None,
			Transform: Matrix3x2.Identity,
			OutputState: CanvasOutputStates.Alpha,
			Material: CanvasMaterials.Color,
			Sampling: TexSampling.Nearest
		), p);
		Assert.Equal(p with { ColorAttachmentOps = ColorAttachmentOps.Load }, CanvasParams.Primary(ColorAttachmentOps.Load));
	}

	// many draws with alternating textures make many draw ranges; the second frame has to reuse the
	// first frame's arena and buffers rather than allocate per draw or per frame
	[Fact]
	public void ArenasAreReusedAcrossFrames() {
		using Texture2d a = solid(red), b = solid(green);
		CanvasArenaPool pool = CanvasDeviceResources.For(Device).ArenaPool;
		void frame() => render(black, canvas => {
			Canvas cv = canvas.Canvas;
			for (int i = 0; i < 1000; i++)
				cv.Texture((i & 1) == 0 ? a : b, new RectF(i % 4, i / 4 % 4, 1, 1));
		});
		frame();
		int arenas = pool.ArenaCount;
		frame();
		frame();
		Assert.Equal(arenas, pool.ArenaCount);
	}

	// more vertices than a default chunk holds, as one draw
	private static readonly Vector2[] hugeTriangleList = new Vector2[CanvasArena.DefaultChunkVertices + 3 - CanvasArena.DefaultChunkVertices % 3];

	// runs body with policy set on the shared device, restoring the previous policy afterwards
	private void withPolicy(CanvasMemoryPolicy policy, Action body) {
		CanvasMemoryPolicy old = OwnedCanvas.GetMemoryPolicy(Device);
		OwnedCanvas.SetMemoryPolicy(Device, policy);
		try {
			body();
		} finally {
			OwnedCanvas.SetMemoryPolicy(Device, old);
		}
	}

	// frame 1 leaves [small chunk, big chunk]; frame 2 draws the big draw first, which has to find
	// the big leftover rather than allocate another one
	[Fact]
	public void BigLeftoverChunksAreFoundWhenNotNextInLine() {
		CanvasArenaPool pool = CanvasDeviceResources.For(Device).ArenaPool;
		withPolicy(new CanvasMemoryPolicy(int.MaxValue, TimeSpan.MaxValue), () => {
			render(black, canvas => {
				canvas.Canvas.Rect(new RectF(0, 0, 1, 1), SrgbColor32.White);
				canvas.Canvas.TriangleList(hugeTriangleList, SrgbColor32.White);
			});
			int chunks = pool.TotalGeometryChunkCount;
			render(black, canvas => {
				canvas.Canvas.TriangleList(hugeTriangleList, SrgbColor32.White);
				canvas.Canvas.Rect(new RectF(0, 0, 1, 1), SrgbColor32.White);
			});
			Assert.Equal(chunks, pool.TotalGeometryChunkCount);
		});
	}

	[Fact]
	public void UnusedChunksAndArenasAreReleased() {
		CanvasArenaPool pool = CanvasDeviceResources.For(Device).ArenaPool;
		withPolicy(new CanvasMemoryPolicy(0, TimeSpan.Zero), () => {
			// two canvases in one frame need two arenas
			using (GpuTexture target = GpuRig.Target(Device, 4, 4)) {
				using RenderFrame frame = new(Device, new GpuRig.FakeOutput(target));
				using (OwnedCanvas a = new(frame, baseParams(black)))
					a.Canvas.TriangleList(hugeTriangleList, SrgbColor32.White);
				using (OwnedCanvas b = new(frame, baseParams(black)))
					b.Canvas.Rect(new RectF(0, 0, 1, 1), SrgbColor32.White);
				frame.Submit();
			}
			Assert.True(pool.ArenaCount >= 2);
			// one small canvas: renting it releases the other arena, and its big chunk goes once a
			// canvas didn't need it
			render(black, canvas => canvas.Canvas.Rect(new RectF(0, 0, 1, 1), SrgbColor32.White));
			render(black, canvas => canvas.Canvas.Rect(new RectF(0, 0, 1, 1), SrgbColor32.White));
			Assert.Equal(1, pool.ArenaCount);
			Assert.Equal(1, pool.TotalGeometryChunkCount);
		});
	}

	[Fact]
	public void MemoryPolicyRejectsNegativeValues() {
		Assert.Throws<ArgumentOutOfRangeException>(() => OwnedCanvas.SetMemoryPolicy(Device, new CanvasMemoryPolicy(-1, TimeSpan.Zero)));
		Assert.Throws<ArgumentOutOfRangeException>(() => OwnedCanvas.SetMemoryPolicy(Device, new CanvasMemoryPolicy(0, TimeSpan.FromSeconds(-1))));
		Assert.Equal(CanvasMemoryPolicy.Default, OwnedCanvas.GetMemoryPolicy(Device));
	}

	[Fact]
	public void DrawingIntoTheActiveTargetIsRejected() {
		using RenderTarget2d rt = new(Device, 4, 4);
		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(target: rt)) {
				Exception? e = null;
				try {
					cv.Texture(rt, new RectF(0, 0, 4, 4));
				} catch (InvalidOperationException ex) {
					e = ex;
				}
				Assert.NotNull(e);
			}
		});
	}

	[Fact]
	public void OverridesMustBeDisposedInReverseOrder() {
		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			CanvasParamsScope outer = cv.PushParams(transform: Matrix3x2.CreateTranslation(1, 0));
			CanvasParamsScope inner = cv.PushParams(transform: Matrix3x2.CreateTranslation(2, 0));
			bool threw = false;
			try {
				outer.Dispose();
			} catch (InvalidOperationException) {
				threw = true;
			}
			Assert.True(threw);
			inner.Dispose();
			// disposing again (or a copy) does nothing
			inner.Dispose();
			outer.Dispose();
			Assert.Equal(Matrix3x2.Identity, cv.CurrentParams.Transform);
			default(CanvasParamsScope).Dispose();
		});
	}

	[Fact]
	public void TooSmallSpansAndBadThicknessAreRejected() {
		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			Vector2[] two = [new(0, 0), new(1, 1)];
			int caught = 0;
			try { cv.ConvexPoly(two, SrgbColor32.White); } catch (ArgumentException) { caught++; }
			try { cv.TriangleStrip(two, SrgbColor32.White); } catch (ArgumentException) { caught++; }
			try { cv.TriangleList(ReadOnlySpan<Vector2>.Empty, SrgbColor32.White); } catch (ArgumentException) { caught++; }
			try { cv.LineList(ReadOnlySpan<Vector2>.Empty, SrgbColor32.White); } catch (ArgumentException) { caught++; }
			try { cv.LineStrip(two.AsSpan(0, 1), SrgbColor32.White); } catch (ArgumentException) { caught++; }
			try { cv.Line(two[0], two[1], SrgbColor32.White, float.NaN); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.Line(two[0], two[1], SrgbColor32.White, float.PositiveInfinity); } catch (ArgumentOutOfRangeException) { caught++; }
			Assert.Equal(7, caught);
		});
	}

	[Fact]
	public void RenderTargetsRejectMismatchedFormats() {
		Assert.Throws<ArgumentException>(() => new RenderTarget2d(Device, new RenderTarget2dCreateParams(4, 4, TextureFormat.Depth32Float)));
		Assert.Throws<ArgumentException>(() => new RenderTarget2d(Device, new RenderTarget2dCreateParams(4, 4, TextureFormat.Rgba8Unorm, TextureFormat.Stencil8)));
		using RenderTarget2d withDepth = new(Device, new RenderTarget2dCreateParams(4, 4, TextureFormat.Rgba8Unorm, TextureFormat.Depth24PlusStencil8));
		Assert.Throws<ArgumentNullException>(() => withDepth.CreateFilteringDepthBindGroup(null!));
		using GpuSampler comparison = Device.CreateSampler(SamplerStates.LinearClamp with { Compare = CompareFunction.Less });
		using GpuBindGroup group = withDepth.CreateComparisonDepthBindGroup(comparison);
		Assert.Throws<ArgumentNullException>(() => withDepth.CreateColorBindGroup(null!));
		using GpuSampler linear = Device.CreateSampler(SamplerStates.LinearClamp);
		using GpuBindGroup colorGroup = withDepth.CreateColorBindGroup(linear);
	}

	[Fact]
	public void DisposedCanvasThrows() {
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using RenderFrame frame = new(Device, new GpuRig.FakeOutput(target));
		OwnedCanvas canvas = new(frame, baseParams(black));
		canvas.Dispose();
		Assert.Throws<ObjectDisposedException>(() => { _ = canvas.Canvas; });
		Assert.Throws<InvalidDefaultException>(() => default(Canvas).Rect(new RectF(0, 0, 1, 1), SrgbColor32.White));
	}
}
