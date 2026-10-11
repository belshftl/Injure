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

	private static CanvasParams baseParams(RawColor32 clear) => new(
		Target: CanvasTarget.Primary,
		ColorAttachmentOps: ColorAttachmentOps.Clear(clear.ToRawF128()),
		Scissor: CanvasScissor.None,
		Transform: Matrix3x2.Identity,
		OutputState: CanvasOutputStates.Alpha,
		Material: CanvasMaterials.Color
	);

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
	}

	[Fact]
	public void DisposedCanvasThrows() {
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using RenderFrame frame = new(Device, new GpuRig.FakeOutput(target));
		OwnedCanvas canvas = new(frame, baseParams(black));
		canvas.Dispose();
		Assert.Throws<ObjectDisposedException>(() => { _ = canvas.Canvas; });
		Assert.Throws<InvalidOperationException>(() => default(Canvas).Rect(new RectF(0, 0, 1, 1), SrgbColor32.White));
	}
}
