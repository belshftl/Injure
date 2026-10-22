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

	private static CanvasParams baseParams(RawColor32 clear, uint size = 4) =>
		CanvasParams.Primary(Viewport.Whole(new Vector2(size, size)), clear.AssumeSrgb());

	// a viewport covering the whole size x size test target at `scale` pixels per unit
	private static Viewport scaled(float scale, uint size = 4) => Viewport.Whole(new Vector2(size, size), scale);

	// draws into a size x size test texture standing in for the primary output and returns its texels
	private byte[] render(RawColor32 clear, Action<OwnedCanvas> draw, uint size = 4) {
		using GpuTexture target = GpuRig.Target(Device, size, size);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			using (OwnedCanvas canvas = new(frame, baseParams(clear, size)))
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
			cv.Texture(tex, new TexDraw(new Vector2(0, 0)) { Source = TexRegion.Texels(new RectF(1, 0, 1, 1)) });
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
		static TexDraw row(int y, TexAddressing wrap) => new(new RectF(0, y, 4, 1)) {
			Source = TexRegion.Uv(new RectF(0, 0, 2, 1)),
			Sampling = new TexSampling(TexFilter.Nearest, wrap),
		};

		using Texture2d tex = redBlue();
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			cv.Texture(tex, row(0, TexAddressing.Clamp));
			cv.Texture(tex, row(1, TexAddressing.Repeat));
			cv.Texture(tex, row(2, TexAddressing.MirrorRepeat));
		});
		Assert.Equal([red, blue, blue, blue], Enumerable.Range(0, 4).Select(x => GpuRig.At(px, 4, (uint)x, 0)));
		Assert.Equal([red, blue, red, blue], Enumerable.Range(0, 4).Select(x => GpuRig.At(px, 4, (uint)x, 1)));
		Assert.Equal([red, blue, blue, red], Enumerable.Range(0, 4).Select(x => GpuRig.At(px, 4, (uint)x, 2)));
	}

	// the 2x1 red/blue texture stretched across 5 pixels is a scale of 2.5, so the texel edge is at
	// x = 2.5 + offset and only pixel 2 mixes, by how much of it each texel covers
	[Theory]
	[InlineData(0f, 0.5f)]
	[InlineData(0.25f, 0.25f)]
	public void AreaWeightedMixesByCoverage(float offset, float blueCoverage) {
		using Texture2d tex = redBlue();
		byte[] px = render(black, canvas => {
			canvas.Canvas.Texture(tex, new TexDraw(new RectF(offset, 0, 5, 5)) {
				Sampling = TexSampling.AreaWeighted,
			});
		}, size: 5);
		Assert.Equal(red, GpuRig.At(px, 5, 0, 2));
		Assert.Equal(red, GpuRig.At(px, 5, 1, 2));
		Assert.Equal(blue, GpuRig.At(px, 5, 3, 2));
		Assert.Equal(blue, GpuRig.At(px, 5, 4, 2));
		RawColor32 mix = GpuRig.At(px, 5, 2, 2);
		Assert.InRange(mix.R, 255 * (1f - blueCoverage) - 3, 255 * (1f - blueCoverage) + 3);
		Assert.InRange(mix.B, 255 * blueCoverage - 3, 255 * blueCoverage + 3);
	}

	// at an integer scale with texel edges on pixel boundaries, there's nothing to mix
	[Fact]
	public void AreaWeightedAtIntegerScalesMatchesNearest() {
		using Texture2d tex = redBlue();
		byte[] px = render(black, canvas => {
			canvas.Canvas.Texture(tex, new TexDraw(new RectF(0, 0, 4, 1)) {
				Sampling = TexSampling.AreaWeighted,
			});
		});
		assertRow(px, 0, linear: false);
	}

	[Fact]
	public void AreaWeightedDoesntAffectUntexturedDraws() {
		using Texture2d tex = redBlue();
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(sampling: TexSampling.AreaWeighted)) {
				cv.Texture(tex, new RectF(0, 0, 4, 1));
				cv.Rect(new RectF(0, 1, 4, 3), green.AssumeSrgb());
			}
		});
		for (uint y = 1; y < 4; y++)
			for (uint x = 0; x < 4; x++)
				Assert.Equal(green, GpuRig.At(px, 4, x, y));
	}

	[Fact]
	public void SdfTexturesRequireLinearSampling() {
		using Texture2d tex = redBlue();
		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(material: CanvasMaterial.Sdf(new SdfParams(4f)))) {
				TexDraw areaWeighted = new(new RectF(0, 0, 4, 4)) { Sampling = TexSampling.AreaWeighted };
				Assert.Throws<InvalidOperationException>(() => canvas.Canvas.Texture(tex, new RectF(0, 0, 4, 4)));
				Assert.Throws<InvalidOperationException>(() => canvas.Canvas.Texture(tex, areaWeighted));
				cv.Texture(tex, new TexDraw(new RectF(0, 0, 4, 4)) { Sampling = TexSampling.Linear });
			}
		});
	}

	[Fact]
	public void TransformsApplyPerDraw() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateTranslation(2, 0))))
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
			using (cv.PushParams(scissor: CanvasScissorOverride.Set(new RectF(0, 0, 2, 2))))
				cv.Rect(new RectF(0, 0, 4, 4), red.AssumeSrgb());
			cv.Rect(new RectF(0, 3, 4, 1), blue.AssumeSrgb());
		});
		Assert.Equal(red, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 3, 0));
		Assert.Equal(blue, GpuRig.At(px, 4, 3, 3));
	}

	[Fact]
	public void TransformOverridesSetOrMultiply() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateScale(2)))) {
				// applied before the scale, so the translation is scaled too: lands at (2, 0)
				using (cv.PushParams(transform: CanvasTransformOverride.Multiply(Matrix3x2.CreateTranslation(1, 0))))
					cv.Rect(new RectF(0, 0, 1, 1), red.AssumeSrgb());
				// replaces the scale rather than combining with it
				using (cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateTranslation(0, 3))))
					cv.Rect(new RectF(0, 0, 1, 1), blue.AssumeSrgb());
			}
		});
		Assert.Equal(black, GpuRig.At(px, 4, 1, 0));
		Assert.Equal(red, GpuRig.At(px, 4, 2, 0));
		Assert.Equal(red, GpuRig.At(px, 4, 3, 1));
		Assert.Equal(blue, GpuRig.At(px, 4, 0, 3));
		Assert.Equal(black, GpuRig.At(px, 4, 1, 3));
	}

	[Fact]
	public void ScissorOverridesIntersectAndClear() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(scissor: CanvasScissorOverride.Set(new RectF(0, 0, 3, 3)))) {
				using (cv.PushParams(scissor: CanvasScissorOverride.Intersect(new RectF(1, 1, 3, 3))))
					cv.Rect(new RectF(0, 0, 4, 4), red.AssumeSrgb());
				using (cv.PushParams(scissor: CanvasScissorOverride.NoScissor))
					cv.Rect(new RectF(3, 0, 1, 1), blue.AssumeSrgb());
			}
		});
		Assert.Equal(black, GpuRig.At(px, 4, 0, 0));
		Assert.Equal(red, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(red, GpuRig.At(px, 4, 2, 2));
		Assert.Equal(black, GpuRig.At(px, 4, 3, 3));
		Assert.Equal(blue, GpuRig.At(px, 4, 3, 0));
	}

	[Fact]
	public void IntersectWithoutAScissorSetsIt() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(scissor: CanvasScissorOverride.Intersect(new RectF(0, 0, 2, 2))))
				cv.Rect(new RectF(0, 0, 4, 4), red.AssumeSrgb());
		});
		Assert.Equal(red, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 2, 2));
	}

	[Fact]
	public void DrawableAreaSizeComesFromTheViewport() {
		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			Assert.Equal(4f, cv.Width);
			Assert.Equal(1f, cv.PixelsPerUnit);
			using (cv.PushParams(target: CanvasTarget.Primary(scaled(2)))) {
				Assert.Equal(2f, cv.Width);
				Assert.Equal(2f, cv.Height);
				Assert.Equal(2f, cv.PixelsPerUnit);
				Assert.Equal(4u, cv.TargetPixelWidth);
				using (cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateScale(10))))
					Assert.Equal(2f, cv.Width);
			}
			// a 2x1 area letterboxed into the 4x4 target at scale 2, with bars above and below
			using (cv.PushParams(target: CanvasTarget.Primary(Viewport.Letterbox(new Vector2(2, 1), new Vector2(4, 4), false)))) {
				Assert.Equal(2f, cv.Width);
				Assert.Equal(1f, cv.Height);
				Assert.Equal(4u, cv.TargetPixelHeight);
			}
		});
	}

	// the 2x1 letterbox covers pixel rows 1 and 2; rows 0 and 3 are bars
	[Fact]
	public void DrawsAreClippedToTheDrawableArea() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(target: CanvasTarget.Primary(Viewport.Letterbox(new Vector2(2, 1), new Vector2(4, 4), false)))) {
				// a scissor larger than the drawable area doesn't widen the clip
				using (cv.PushParams(scissor: CanvasScissorOverride.Set(new RectF(-10, -10, 100, 100))))
					cv.Rect(new RectF(-10, -10, 100, 100), red.AssumeSrgb());
				// at 2 pixels per unit, this scissor's far edges are past float range in pixels
				using (cv.PushParams(scissor: CanvasScissorOverride.Set(new RectF(0, 0, 3e38f, 3e38f))))
					cv.Rect(new RectF(0, 0, 1, 1), red.AssumeSrgb());
				cv.Clear(red.AssumeSrgb());
				// a scissor in drawable units maps through the viewport: unit (1, 0) is pixels (2..4, 1..3)
				using (cv.PushParams(scissor: CanvasScissorOverride.Set(new RectF(1, 0, 1, 1))))
					cv.Clear(blue.AssumeSrgb());
			}
		});
		for (uint x = 0; x < 4; x++) {
			Assert.Equal(black, GpuRig.At(px, 4, x, 0));
			Assert.Equal(black, GpuRig.At(px, 4, x, 3));
		}
		Assert.Equal(red, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(red, GpuRig.At(px, 4, 0, 2));
		Assert.Equal(blue, GpuRig.At(px, 4, 2, 1));
		Assert.Equal(blue, GpuRig.At(px, 4, 3, 2));
	}

	// the scissor itself stays None, but the clip moves with the viewport
	[Fact]
	public void ChangingOnlyTheViewportUpdatesTheClip() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			Viewport left = new(Vector2.Zero, 1f, new Vector2(2, 4));
			Viewport right = new(new Vector2(2, 0), 1f, new Vector2(2, 4));
			using (cv.PushParams(target: CanvasTarget.Primary(left)))
				cv.Rect(new RectF(0, 0, 4, 1), red.AssumeSrgb());
			using (cv.PushParams(target: CanvasTarget.Primary(right)))
				cv.Rect(new RectF(0, 1, 4, 1), blue.AssumeSrgb());
		});
		Assert.Equal(red, GpuRig.At(px, 4, 1, 0));
		Assert.Equal(black, GpuRig.At(px, 4, 2, 0));
		Assert.Equal(black, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(blue, GpuRig.At(px, 4, 2, 1));
		Assert.Equal(blue, GpuRig.At(px, 4, 3, 1));
	}

	[Fact]
	public void ViewportAppliesAfterTheTransformAndSurvivesSet() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(target: CanvasTarget.Primary(scaled(2))))
			using (cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateTranslation(1, 0))))
				// translated, then scaled: lands at (2, 0) with size 2
				cv.Rect(new RectF(0, 0, 1, 1), red.AssumeSrgb());
		});
		Assert.Equal(black, GpuRig.At(px, 4, 1, 0));
		Assert.Equal(red, GpuRig.At(px, 4, 2, 0));
		Assert.Equal(red, GpuRig.At(px, 4, 3, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 3, 2));
	}

	// a new pass would be begun with the base params' clear op and wipe the red rect
	[Fact]
	public void ChangingOnlyTheViewportKeepsTheRenderPass() {
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			cv.Rect(new RectF(0, 0, 1, 1), red.AssumeSrgb());
			using (cv.PushParams(target: CanvasTarget.Primary(scaled(2))))
				cv.Rect(new RectF(1, 1, 1, 1), blue.AssumeSrgb());
			cv.Rect(new RectF(0, 3, 1, 1), green.AssumeSrgb());
		});
		Assert.Equal(red, GpuRig.At(px, 4, 0, 0));
		Assert.Equal(blue, GpuRig.At(px, 4, 2, 2));
		Assert.Equal(blue, GpuRig.At(px, 4, 3, 3));
		Assert.Equal(green, GpuRig.At(px, 4, 0, 3));
	}

	[Fact]
	public void OffscreenTargetsApplyTheirViewport() {
		using RenderTarget2d rt = new(Device, 4, 4);
		byte[] px = render(black, canvas => {
			Canvas cv = canvas.Canvas;
			var target = CanvasTarget.Offscreen(rt, new Viewport(new Vector2(2, 0), 1f, new Vector2(2, 4)));
			using (cv.PushParams(target: target, colorAttachmentOps: ColorAttachmentOps.Clear(blue.ToRawF128())))
				cv.Rect(new RectF(0, 0, 2, 4), red.AssumeSrgb());
			cv.Texture(rt, Vector2.Zero);
		});
		Assert.Equal(blue, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(red, GpuRig.At(px, 4, 2, 1));
		Assert.Equal(red, GpuRig.At(px, 4, 3, 2));
	}

	// Scale(1e-20) has a nonzero determinant, but its inverse overflows, so by our definition it's
	// non-invertible
	// at 1e10 pixels per unit, the effective transform is Scale(1e-10), whose inverse doesn't
	// overflow, so only the check of the transform by itself rejects it, since the transform by
	// itself must stay invertible too
	// Set overrides are checked on their own, so this multiplies two invertible ones into it
	[Fact]
	public void TransformsMustBeInvertibleOnTheirOwn() {
		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(target: CanvasTarget.Primary(scaled(1e10f))))
			using (cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateScale(1e-10f)))) {
				var tiny = CanvasTransformOverride.Multiply(Matrix3x2.CreateScale(1e-10f));
				Assert.Throws<ArgumentException>(() => canvas.Canvas.PushParams(transform: tiny));
				Assert.True(MatrixInvert.IsInvertible(Matrix3x2.CreateScale(1e-20f) * cv.CurrentViewport.ToPixelsMatrix));
			}
		});
	}

	[Fact]
	public void DefaultTargetsAreRejected() {
		render(black, canvas => {
			Assert.Throws<ArgumentInvalidDefaultException>(() => {
				using (canvas.Canvas.PushParams(target: default(CanvasTarget))) { }
			});
		});
		Assert.Throws<InvalidDefaultException>(() => default(CanvasTarget).IsPrimary);
		Assert.Throws<InvalidDefaultException>(() => default(CanvasTarget).Viewport);
		Assert.Throws<ArgumentInvalidDefaultException>(() => CanvasTarget.Primary(default));
		using RenderTarget2d rt = new(Device, 4, 4);
		Assert.Throws<ArgumentInvalidDefaultException>(() => CanvasTarget.Offscreen(rt, default));
	}

	[Fact]
	public void NonInvertibleMatricesAreRejected() {
		// 1e20 squared overflows the determinant; Matrix3x2.Invert alone would "invert" it to zeros
		Matrix3x2[] bad = [
			Matrix3x2.CreateScale(0),
			Matrix3x2.CreateScale(1, 0),
			Matrix3x2.CreateScale(1e20f),
			Matrix3x2.CreateTranslation(float.NaN, 0),
		];
		foreach (Matrix3x2 m in bad) {
			Assert.Throws<ArgumentOutOfRangeException>(() => CanvasTransformOverride.Set(m));
			Assert.Throws<ArgumentOutOfRangeException>(() => CanvasTransformOverride.Multiply(m));
		}
	}

	[Fact]
	public void NonInvertibleProductsAreRejected() {
		// each factor is invertible, but the product's determinant (1e-40) has no finite reciprocal
		var tiny = Matrix3x2.CreateScale(1e-10f);
		Viewport tinyViewport = new(Vector2.Zero, 1e-10f, new Vector2(1, 1));
		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			using (cv.PushParams(transform: CanvasTransformOverride.Set(tiny))) {
				Assert.Throws<ArgumentException>(() => {
					using (canvas.Canvas.PushParams(transform: CanvasTransformOverride.Multiply(tiny))) { }
				});
				Assert.Throws<ArgumentException>(() => {
					using (canvas.Canvas.PushParams(target: CanvasTarget.Primary(tinyViewport))) { }
				});
			}
		});
	}

	[Fact]
	public void DefaultTransformOverridesAreRejected() {
		render(black, canvas => {
			Assert.Throws<ArgumentInvalidDefaultException>(() => {
				using (canvas.Canvas.PushParams(transform: default(CanvasTransformOverride))) { }
			});
		});
		Assert.Throws<InvalidDefaultException>(() => default(CanvasTransformOverride).Matrix);
		Assert.Throws<InvalidDefaultException>(() => default(CanvasTransformOverride).Kind);
	}

	[Fact]
	public void DefaultScissorOverridesAreRejected() {
		render(black, canvas => {
			Assert.Throws<ArgumentInvalidDefaultException>(() => {
				using (canvas.Canvas.PushParams(scissor: default(CanvasScissorOverride))) { }
			});
		});
	}

	[Fact]
	public void DefaultMaterialsAreRejected() {
		render(black, canvas => {
			Assert.Throws<ArgumentInvalidDefaultException>(() => {
				using (canvas.Canvas.PushParams(material: default(CanvasMaterial))) { }
			});
		});
		Assert.Throws<InvalidDefaultException>(() => default(CanvasMaterial).TextureInterpretation);
		Assert.Throws<InvalidDefaultException>(() => default(CanvasMaterial).SdfParams);
	}

	[Fact]
	public void TargetEqualityIncludesTheViewport() {
		using RenderTarget2d rt = new(Device, 4, 4);
		using RenderTarget2d other = new(Device, 4, 4);
		Viewport two = scaled(2), one = scaled(1);

		Assert.Equal(CanvasTarget.Primary(two), CanvasTarget.Primary(two));
		Assert.NotEqual(CanvasTarget.Primary(two), CanvasTarget.Primary(one));
		Assert.True(CanvasTarget.Primary(two).SameRenderTarget(CanvasTarget.Primary(one)));

		Assert.Equal(CanvasTarget.Offscreen(rt, one), (CanvasTarget)rt);
		Assert.Equal(CanvasTarget.Offscreen(rt), (CanvasTarget)rt);
		Assert.NotEqual(CanvasTarget.Offscreen(rt, two), (CanvasTarget)rt);
		Assert.True(CanvasTarget.Offscreen(rt, two).SameRenderTarget(rt));
		Assert.False(((CanvasTarget)rt).SameRenderTarget(other));
		Assert.False(((CanvasTarget)rt).SameRenderTarget(CanvasTarget.Primary(one)));
	}

	[Fact]
	public void TransformGettersReflectTheParamsStack() {
		render(black, canvas => {
			Canvas cv = canvas.Canvas;
			Viewport vp = new(new Vector2(1, 0), 2f, new Vector2(1, 2));
			var translate = Matrix3x2.CreateTranslation(1, 0);
			using (cv.PushParams(target: CanvasTarget.Primary(vp)))
			using (cv.PushParams(transform: CanvasTransformOverride.Set(translate))) {
				Assert.Equal(translate, cv.CurrentTransform);
				Assert.Equal(vp, cv.CurrentViewport);
				Assert.Equal(translate * vp.ToPixelsMatrix, cv.EffectiveTransform);
			}
			Assert.Equal(Matrix3x2.Identity, cv.CurrentTransform);
			Assert.Equal(scaled(1), cv.CurrentViewport);
			Assert.Equal(Matrix3x2.Identity, cv.EffectiveTransform);
		});
	}

	// 4x4 checkerboard of a and b
	private Texture2d checker(RawColor32 a, RawColor32 b, Texture2dMipFilter? mips) {
		Texture2d tex = new(Device, new Texture2dParams(4, 4, Mips: mips));
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
		using Texture2d oneByOne = new(Device, new Texture2dParams(1, 1, Mips: Texture2dMipFilter.Raw));
		using Texture2d wide = new(Device, new Texture2dParams(5, 3, Mips: Texture2dMipFilter.Raw));
		using Texture2d big = new(Device, new Texture2dParams(256, 64, Mips: Texture2dMipFilter.Color));
		Assert.Equal(1u, oneByOne.MipLevelCount);
		Assert.Equal(3u, wide.MipLevelCount);
		Assert.Equal(9u, big.MipLevelCount);
		Assert.Equal(Texture2dMipFilter.Color, big.MipFilter);

		using Texture2d single = new(Device, 5, 3);
		Assert.Equal(1u, single.MipLevelCount);
		Assert.Null(single.MipFilter);
		Assert.Throws<InvalidOperationException>(single.GenerateMips);
		Assert.Throws<ArgumentInvalidDefaultException>(() => new Texture2d(Device, new Texture2dParams(4, 4, Mips: default(Texture2dMipFilter))));
		Assert.Throws<ArgumentInvalidDefaultException>(() => new Texture2d(Device, new Texture2dParams(4, 4, Format: default(Texture2dFormat))));
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
			cv.Texture(topRow, new TexDraw(new Vector2(1, 0)) { Source = TexRegion.Texels(new RectF(1, 0, 1, 1)) });
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
				new(new Vector2(0, 0)) { Source = TexRegion.Texels(new RectF(1, 0, 1, 1)) },
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
			using (cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateScale(0.25f))))
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
			using (cv.PushParams(scissor: CanvasScissorOverride.Set(new RectF(0, 0, 2, 2))))
				cv.Clear(red.AssumeSrgb());
		});
		Assert.Equal(red, GpuRig.At(px, 4, 1, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 2, 1));
		Assert.Equal(black, GpuRig.At(px, 4, 1, 2));
	}

	[Fact]
	public void PrimaryFactoryFillsInTheCommonParams() {
		Viewport vp = scaled(2);
		var p = CanvasParams.Primary(vp, SrgbColor32.Red);
		Assert.Equal(new CanvasParams(
			Target: CanvasTarget.Primary(vp),
			ColorAttachmentOps: ColorAttachmentOps.Clear(SrgbColor32.Red.ToRawF128()),
			Scissor: CanvasScissor.None,
			Transform: Matrix3x2.Identity,
			OutputState: CanvasOutputStates.Alpha,
			Material: CanvasMaterial.Color,
			Sampling: TexSampling.Nearest
		), p);
		Assert.Equal(p with { ColorAttachmentOps = ColorAttachmentOps.Load }, CanvasParams.Primary(vp, ColorAttachmentOps.Load));
	}

	[Fact]
	public void FramePrimaryFactoryCoversTheWholeOutput() {
		using GpuTexture target = GpuRig.Target(Device, 6, 4);
		using RenderFrame frame = new(Device, new GpuRig.FakeOutput(target));
		var p = CanvasParams.Primary(frame, SrgbColor32.Red, pixelsPerUnit: 2f);
		Assert.Equal(CanvasParams.Primary(Viewport.Whole(new Vector2(6, 4), 2f), SrgbColor32.Red), p);
		Assert.Equal(new Vector2(3, 2), p.Target.Viewport.Size);
		Assert.Equal(Viewport.Whole(new Vector2(6, 4)), CanvasParams.Primary(frame, ColorAttachmentOps.Load).Target.Viewport);
		Assert.Throws<ArgumentNullException>(() => CanvasParams.Primary(null!, SrgbColor32.Red));
		Assert.Throws<ArgumentOutOfRangeException>(() => CanvasParams.Primary(frame, SrgbColor32.Red, 0f));
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
			CanvasParamsScope outer = cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateTranslation(1, 0)));
			CanvasParamsScope inner = cv.PushParams(transform: CanvasTransformOverride.Set(Matrix3x2.CreateTranslation(2, 0)));
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

	// ==========================================================================
	// shapes
	private static readonly SrgbColor32 halfRed = new(255, 0, 0, 128);

	private byte[] render16(Action<Canvas> draw) => render(black, canvas => draw(canvas.Canvas), 16);
	private static RawColor32 at16(byte[] px, uint x, uint y) => GpuRig.At(px, 16, x, y);
	private static bool isHalf(RawColor32 c) => c.R is >= 125 and <= 131 && c.G == 0 && c.B == 0;

	// checks that a translucent red draw over black blended exactly once wherever it drew anything;
	// returns how many pixels it drew
	private static int assertBlendedOnce(byte[] px) {
		int drawn = 0;
		for (uint y = 0; y < 16; y++) {
			for (uint x = 0; x < 16; x++) {
				RawColor32 c = at16(px, x, y);
				if (c == black)
					continue;
				Assert.True(isHalf(c), $"pixel ({x}, {y}) is {c}");
				drawn++;
			}
		}
		Assert.NotEqual(0, drawn);
		return drawn;
	}

	[Fact]
	public void RectOutlinesLieInsideTheRect() {
		byte[] px = render16(cv => cv.RectOutline(new RectF(2, 2, 12, 12), halfRed, 2f));
		assertBlendedOnce(px);
		foreach ((uint x, uint y) in (ReadOnlySpan<(uint, uint)>)[(2, 2), (3, 3), (8, 2), (13, 13), (13, 8), (2, 12)])
			Assert.True(isHalf(at16(px, x, y)), $"({x}, {y})");
		foreach ((uint x, uint y) in (ReadOnlySpan<(uint, uint)>)[(1, 1), (14, 14), (4, 4), (8, 8), (11, 11)])
			Assert.Equal(black, at16(px, x, y));

		// too thick to have a hole; fills the rect
		px = render16(cv => cv.RectOutline(new RectF(2, 2, 12, 6), halfRed, 3f));
		Assert.Equal(12 * 6, assertBlendedOnce(px));
	}

	[Fact]
	public void CirclesAndEllipsesCoverTheirArea() {
		byte[] px = render16(cv => cv.Circle(new Vector2(8, 8), 6f, red.AssumeSrgb()));
		Assert.Equal(red, at16(px, 8, 8));
		Assert.Equal(red, at16(px, 8, 3));
		Assert.Equal(red, at16(px, 13, 8));
		Assert.Equal(black, at16(px, 2, 2));
		Assert.Equal(black, at16(px, 0, 8));

		px = render16(cv => cv.Ellipse(new Vector2(8, 8), new Vector2(7, 3), red.AssumeSrgb()));
		Assert.Equal(red, at16(px, 2, 8));
		Assert.Equal(red, at16(px, 8, 10));
		Assert.Equal(black, at16(px, 8, 3));
		Assert.Equal(black, at16(px, 3, 5));

		// 4 segments is a diamond, leaving out parts of the circle
		px = render16(cv => cv.Circle(new Vector2(8, 8), 7f, red.AssumeSrgb(), segments: 4));
		Assert.Equal(red, at16(px, 5, 4));
		Assert.Equal(black, at16(px, 3, 4));

		// radius 0 draws nothing
		px = render16(cv => cv.Circle(new Vector2(8, 8), 0f, red.AssumeSrgb()));
		Assert.Equal(black, at16(px, 8, 8));
	}

	// single-color shapes add their geometry through a different path than per-vertex-color ones
	[Fact]
	public void SingleColorShapesMapThroughTheViewport() {
		// at 4 pixels per unit, a circle at (2, 2) with radius 1.5 covers pixels within 6 of (8, 8)
		byte[] px = render16(cv => {
			using (cv.PushParams(target: CanvasTarget.Primary(scaled(4, 16))))
				cv.Circle(new Vector2(2, 2), 1.5f, red.AssumeSrgb());
		});
		Assert.Equal(red, at16(px, 8, 8));
		Assert.Equal(red, at16(px, 8, 3));
		Assert.Equal(black, at16(px, 1, 1));
	}

	[Fact]
	public void CircleAndEllipseOutlinesLieInsideTheShape() {
		byte[] px = render16(cv => cv.CircleOutline(new Vector2(8, 8), 6f, halfRed, 2f));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 8, 2)));
		Assert.True(isHalf(at16(px, 13, 8)));
		Assert.Equal(black, at16(px, 8, 8));
		Assert.Equal(black, at16(px, 8, 5));
		Assert.Equal(black, at16(px, 8, 1));

		px = render16(cv => cv.EllipseOutline(new Vector2(8, 8), new Vector2(7, 4), halfRed, 1.5f));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 1, 8)));
		Assert.True(isHalf(at16(px, 8, 4)));
		Assert.Equal(black, at16(px, 8, 8));
		Assert.Equal(black, at16(px, 4, 8));

		// thicker than the smaller radius makes a filled shape
		px = render16(cv => cv.EllipseOutline(new Vector2(8, 8), new Vector2(7, 3), halfRed, 3f));
		Assert.True(isHalf(at16(px, 8, 8)));
	}

	[Fact]
	public void RoundedRectsRoundTheirCorners() {
		byte[] px = render16(cv => cv.RoundedRect(new RectF(0, 0, 16, 16), 6f, red.AssumeSrgb()));
		Assert.Equal(black, at16(px, 0, 0));
		Assert.Equal(black, at16(px, 1, 1));
		Assert.Equal(black, at16(px, 15, 15));
		Assert.Equal(red, at16(px, 2, 2));
		Assert.Equal(red, at16(px, 8, 0));
		Assert.Equal(red, at16(px, 0, 8));
		Assert.Equal(red, at16(px, 8, 8));

		// the radius is limited to half the shorter side, making a pill
		px = render16(cv => cv.RoundedRect(new RectF(0, 0, 16, 8), 100f, red.AssumeSrgb()));
		Assert.Equal(black, at16(px, 0, 0));
		Assert.Equal(red, at16(px, 8, 0));
		Assert.Equal(red, at16(px, 0, 4));
		Assert.Equal(black, at16(px, 8, 8));

		// radius 0 makes a plain rect
		px = render16(cv => cv.RoundedRect(new RectF(0, 0, 16, 16), 0f, red.AssumeSrgb()));
		Assert.Equal(red, at16(px, 0, 0));
		Assert.Equal(red, at16(px, 15, 15));
	}

	[Fact]
	public void RoundedRectOutlinesFollowTheCorners() {
		byte[] px = render16(cv => cv.RoundedRectOutline(new RectF(0, 0, 16, 16), 6f, halfRed, 2f));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 8, 0)));
		Assert.True(isHalf(at16(px, 8, 1)));
		Assert.True(isHalf(at16(px, 2, 2)));
		Assert.True(isHalf(at16(px, 15, 8)));
		Assert.Equal(black, at16(px, 0, 0));
		Assert.Equal(black, at16(px, 8, 2));
		Assert.Equal(black, at16(px, 4, 4));
		Assert.Equal(black, at16(px, 8, 8));

		// thicker than the radius, so the inner corners are sharp
		px = render16(cv => cv.RoundedRectOutline(new RectF(0, 0, 16, 16), 2f, halfRed, 4f));
		assertBlendedOnce(px);
		Assert.Equal(black, at16(px, 0, 0));
		Assert.True(isHalf(at16(px, 3, 3)));
		Assert.Equal(black, at16(px, 4, 4));
	}

	[Fact]
	public void PiesCoverTheirSector() {
		Vector2 c = new(8, 8);
		// +X toward +Y: the bottom right quadrant on the screen
		byte[] px = render16(cv => cv.Pie(c, 7f, 0f, MathF.PI / 2, halfRed));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 11, 11)));
		Assert.Equal(black, at16(px, 4, 4));
		Assert.Equal(black, at16(px, 11, 4));
		Assert.Equal(black, at16(px, 4, 11));

		px = render16(cv => cv.Pie(c, 7f, 0f, -MathF.PI / 2, halfRed));
		Assert.True(isHalf(at16(px, 11, 4)));
		Assert.Equal(black, at16(px, 11, 11));

		px = render16(cv => cv.Pie(c, 7f, 1f, 3 * MathF.Tau, halfRed));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 4, 4)));
		Assert.True(isHalf(at16(px, 11, 11)));
	}

	[Fact]
	public void ArcsAreCenteredOnTheCircle() {
		Vector2 c = new(8, 8);
		byte[] px = render16(cv => cv.Arc(c, 5f, 0f, MathF.PI, halfRed, 2f));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 8, 13)));
		Assert.True(isHalf(at16(px, 3, 9)));
		Assert.Equal(black, at16(px, 8, 2));
		Assert.Equal(black, at16(px, 8, 8));
		Assert.Equal(black, at16(px, 8, 15));

		px = render16(cv => cv.Arc(c, 5f, 0f, MathF.Tau, halfRed, 2f));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 8, 2)));
		Assert.True(isHalf(at16(px, 8, 13)));
	}

	[Fact]
	public void LineStripJoinsDontOverlap() {
		// a right angle: the separate quads of the old strips used to blend twice at the corner,
		// and miss the outer corner
		byte[] px = render16(cv => cv.LineStrip([new(2, 2), new(12, 2), new(12, 12)], halfRed, 4f));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 12, 2)));
		Assert.True(isHalf(at16(px, 13, 0)));
		Assert.Equal(black, at16(px, 1, 2));
		Assert.Equal(black, at16(px, 12, 12));

		// sharp enough that it becomes a bevel
		px = render16(cv => cv.LineStrip([new(2, 6), new(14, 8), new(2, 10)], halfRed, 2f));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 13, 8)));
		Assert.Equal(black, at16(px, 15, 8));
	}

	[Fact]
	public void LineLoopsJoinAtEveryPoint() {
		Vector2[] square = [new(3, 3), new(12, 3), new(12, 12), new(3, 12)];
		byte[] px = render16(cv => cv.LineLoop(square, halfRed, 2f));
		int drawn = assertBlendedOnce(px);
		// the miters reach the outer corners, the first point's included
		Assert.True(isHalf(at16(px, 2, 2)));
		Assert.True(isHalf(at16(px, 12, 12)));
		Assert.Equal(black, at16(px, 7, 7));

		// repeated points, including the first one at the end, change nothing
		Vector2[] repeated = [new(3, 3), new(3, 3), new(12, 3), new(12, 12), new(12, 12), new(3, 12), new(3, 3)];
		Assert.Equal(px, render16(cv => cv.LineLoop(repeated, halfRed, 2f)));
		Assert.Equal(11 * 11 - 7 * 7, drawn);
	}

	[Fact]
	public void LineJoinsShapeTheOuterCorner() {
		Vector2[] corner = [new(2, 5), new(10, 5), new(10, 15)];
		// (13, 2) is only inside the miter's corner, (12, 2) is also inside the round join
		byte[] px = render16(cv => cv.LineStrip(corner, halfRed, 8f, LineJoin.Miter));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 13, 2)));
		Assert.True(isHalf(at16(px, 12, 2)));

		px = render16(cv => cv.LineStrip(corner, halfRed, 8f, LineJoin.Round));
		assertBlendedOnce(px);
		Assert.Equal(black, at16(px, 13, 2));
		Assert.True(isHalf(at16(px, 12, 2)));

		px = render16(cv => cv.LineStrip(corner, halfRed, 8f, LineJoin.Bevel));
		assertBlendedOnce(px);
		Assert.Equal(black, at16(px, 13, 2));
		Assert.Equal(black, at16(px, 12, 2));
		Assert.True(isHalf(at16(px, 11, 4)));

		// sharp turns and loops blend once with round joins too
		assertBlendedOnce(render16(cv => cv.LineStrip([new(2, 6), new(14, 8), new(2, 10)], halfRed, 2f, LineJoin.Round)));
		Vector2[] square = [new(3, 3), new(12, 3), new(12, 12), new(3, 12)];
		px = render16(cv => cv.LineLoop(square, halfRed, 3f, LineJoin.Round));
		assertBlendedOnce(px);
		Assert.Equal(black, at16(px, 1, 1));
		Assert.True(isHalf(at16(px, 2, 3)));
	}

	[Fact]
	public void LineCapsExtendTheEnds() {
		// (1, 8) is inside the round and square caps, (1, 5) only inside the square one
		foreach (bool strip in (ReadOnlySpan<bool>)[false, true]) {
			byte[] draw(LineCap cap) => render16(cv => {
				if (strip)
					cv.LineStrip([new(4, 8), new(12, 8)], halfRed, 6f, cap: cap);
				else
					cv.Line(new Vector2(4, 8), new Vector2(12, 8), halfRed, 6f, cap);
			});
			byte[] px = draw(LineCap.Butt);
			Assert.Equal(8 * 6, assertBlendedOnce(px));
			Assert.Equal(black, at16(px, 3, 8));

			px = draw(LineCap.Round);
			assertBlendedOnce(px);
			Assert.True(isHalf(at16(px, 1, 8)));
			Assert.True(isHalf(at16(px, 14, 8)));
			Assert.Equal(black, at16(px, 1, 5));

			px = draw(LineCap.Square);
			Assert.Equal(14 * 6, assertBlendedOnce(px));
			Assert.True(isHalf(at16(px, 1, 5)));
			Assert.True(isHalf(at16(px, 14, 10)));
		}
	}

	[Fact]
	public void ArcCapsAttachToTheEnds() {
		Vector2 c = new(8, 8);
		// the arc starts at (13, 8) going down, so a start cap reaches up
		byte[] px = render16(cv => cv.Arc(c, 5f, 0f, MathF.PI, halfRed, 4f));
		assertBlendedOnce(px);
		Assert.Equal(black, at16(px, 13, 6));

		px = render16(cv => cv.Arc(c, 5f, 0f, MathF.PI, halfRed, 4f, LineCap.Round));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 13, 6)));
		Assert.True(isHalf(at16(px, 2, 6)));
		Assert.Equal(black, at16(px, 13, 5));

		px = render16(cv => cv.Arc(c, 5f, 0f, MathF.PI, halfRed, 4f, LineCap.Square));
		assertBlendedOnce(px);
		Assert.True(isHalf(at16(px, 14, 6)));
		Assert.True(isHalf(at16(px, 1, 6)));

		// thicker than the diameter, causing a clamp; the caps shrink with the ends rather than reaching
		// past the center
		foreach (LineCap cap in (ReadOnlySpan<LineCap>)[LineCap.Round, LineCap.Square])
			assertBlendedOnce(render16(cv => cv.Arc(c, 2f, 0f, MathF.PI, halfRed, 6f, cap)));
	}

	[Fact]
	public void LineStripsInterpolatePerVertexColors() {
		byte[] px = render16(cv => cv.LineStrip([
			new Vertex2dColor(1, 8, red.AssumeSrgb()),
			new Vertex2dColor(8, 8, green.AssumeSrgb()),
			new Vertex2dColor(15, 8, blue.AssumeSrgb()),
		], 2f));
		Assert.True(at16(px, 1, 7).R > 200);
		Assert.True(at16(px, 8, 7).G > 200);
		Assert.True(at16(px, 14, 7).B > 200);
	}

	[Fact]
	public void BadShapeArgumentsAreRejected() {
		render16(cv => {
			Vector2 c = new(8, 8);
			SrgbColor32 w = SrgbColor32.White;
			int caught = 0;
			try { cv.Circle(c, -1f, w); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.Circle(c, float.NaN, w); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.Circle(c, 1f, w, segments: 2); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.Ellipse(c, new Vector2(1, float.PositiveInfinity), w); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.CircleOutline(c, 1f, w, 0f); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.RoundedRect(new RectF(0, 0, 4, 4), 1f, w, segments: 0); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.RectOutline(new RectF(0, 0, 4, 4), w, -1f); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.Pie(c, 1f, float.NaN, 1f, w); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.Arc(c, 1f, 0f, 1f, w, 1f, segments: 70000); } catch (ArgumentOutOfRangeException) { caught++; }
			try { cv.LineLoop([c], w); } catch (ArgumentException) { caught++; }
			Assert.Equal(10, caught);

			// degenerate input doesn't throw and just draws nothing
			cv.LineStrip([c, c, c], w);
			cv.LineLoop([c, c], w);
			cv.Pie(c, 1f, 0f, 0f, w);
			cv.Arc(c, 1f, 0f, 0f, w);

			// turning straight back gives a half-turn round join
			cv.LineStrip([new(2, 8), new(12, 8), new(2, 8)], w, 3f, LineJoin.Round, LineCap.Round);
		});
	}

	[Fact]
	public void RenderTargetsRejectMismatchedFormats() {
		Assert.Throws<ArgumentException>(() => new RenderTarget2d(Device, new RenderTarget2dParams(4, 4, TextureFormat.Depth32Float)));
		Assert.Throws<ArgumentException>(() => new RenderTarget2d(Device, new RenderTarget2dParams(4, 4, TextureFormat.Rgba8Unorm, TextureFormat.Stencil8)));
		using RenderTarget2d withDepth = new(Device, new RenderTarget2dParams(4, 4, TextureFormat.Rgba8Unorm, TextureFormat.Depth24PlusStencil8));
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
