// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Gpu;
using Injure.Primitives;
using Injure.Tests.Gpu;

namespace Injure.Tests.Draw;

// Canvas takes straight sRGB colors and blends premultiplied linear ones, so it needs targets that
// encode on write (sRGB) or store linear values (float)
public sealed class CanvasColorTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private static readonly SrgbStraight32 clear = new(30, 30, 40);
	private static readonly SrgbStraight32 fill = new(80, 200, 120);

	private static CanvasParams baseParams(SrgbStraight32 clearColor) =>
		CanvasParams.Primary(Viewport.Whole(new Vector2(4, 4)), CanvasLoadOp.Clear(clearColor));

	private byte[] render(SrgbStraight32 clearColor, Action<Canvas> draw) {
		using GpuTexture target = GpuRig.Target(Device, 4, 4, format: TextureFormat.Rgba8UnormSrgb);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			using (OwnedCanvas canvas = new(frame, baseParams(clearColor)))
				draw(canvas.Canvas);
			frame.Submit();
		}
		return GpuRig.Read(Device, target);
	}

	[Fact]
	public void OpaqueColorsComeOutAsGiven() {
		byte[] texels = render(clear, cv => cv.Rect(new RectF(0, 0, 2, 4), fill));
		Assert.Equal(fill.ToRaw(), GpuRig.At(texels, 4, 0, 0));
		Assert.Equal(clear.ToRaw(), GpuRig.At(texels, 4, 3, 3));
	}

	[Fact]
	public void TranslucentColorsBlendInLinearLight() {
		// half of white's light over black is linear 0.5, which encodes to 188 rather than 128
		byte[] texels = render(SrgbStraight32.Black, cv => cv.Rect(new RectF(0, 0, 4, 4), new SrgbStraight32(255, 255, 255, 128)));
		RawColor32 px = GpuRig.At(texels, 4, 0, 0);
		Assert.InRange(px.R, 187, 189);
		Assert.Equal(255, px.A);
	}

	[Fact]
	public void TranslucentClearColorsArePremultiplied() {
		RawColor32 px = GpuRig.At(render(new SrgbStraight32(255, 255, 255, 128), _ => {}), 4, 0, 0);
		Assert.InRange(px.R, 187, 189);
		Assert.Equal(128, px.A);
	}

	public static readonly TheoryData<string, CanvasBlendMode, RawColor32> Presets = new() {
		// half-alpha red over opaque gray 128 (about 0.216 linear); see each preset's formula
		{ "Normal", CanvasBlendMode.Normal, new RawColor32(205, 92, 92, 255) },
		{ "Additive", CanvasBlendMode.Additive, new RawColor32(220, 128, 128, 255) },
		{ "Screen", CanvasBlendMode.Screen, new RawColor32(205, 128, 128, 255) },
		{ "MultiplyOverOpaque", CanvasBlendMode.MultiplyOverOpaque, new RawColor32(128, 92, 92, 255) },
		{ "Subtract", CanvasBlendMode.Subtract, new RawColor32(0, 128, 128, 255) },
		{ "Opaque", CanvasBlendMode.Opaque, new RawColor32(188, 0, 0, 128) },
	};

	[Theory]
	[MemberData(nameof(Presets))]
	public void BlendPresetsFollowTheirFormulas(string name, CanvasBlendMode mode, RawColor32 expected) {
		Assert.Equal(name, mode.ToString());
		byte[] texels = render(new SrgbStraight32(128, 128, 128), cv => {
			using (cv.PushParams(blendMode: mode))
				cv.Rect(new RectF(0, 0, 4, 4), new SrgbStraight32(255, 0, 0, 128));
		});
		RawColor32 px = GpuRig.At(texels, 4, 0, 0);
		Assert.True(
			Math.Abs(px.R - expected.R) <= 1 && Math.Abs(px.G - expected.G) <= 1
				&& Math.Abs(px.B - expected.B) <= 1 && Math.Abs(px.A - expected.A) <= 1,
			$"expected about {expected}, got {px}"
		);
	}

	[Fact]
	public void TargetsNeedToBlendInLinearLight() {
		using GpuTexture unorm = GpuRig.Target(Device, 4, 4);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(unorm)))
			Assert.Throws<ArgumentException>(() => new OwnedCanvas(frame, baseParams(clear)));
		using GpuTexture half = GpuRig.Target(Device, 4, 4, format: TextureFormat.Rgba16Float);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(half))) {
			using (OwnedCanvas canvas = new(frame, baseParams(clear)))
				canvas.Canvas.Rect(new RectF(0, 0, 2, 4), fill);
			frame.Submit();
		}
	}

	// documents the ColorAttachmentOps.ClearValue contract: sRGB targets encode the value
	[Fact]
	public void ClearValueIsEncodedOnlyForSrgbTargets() {
		using GpuTexture unorm = GpuRig.Target(Device, 4, 4);
		using GpuTexture srgb = GpuRig.Target(Device, 4, 4, format: TextureFormat.Rgba8UnormSrgb);
		RawColorF128 half = new(0.5f, 0.5f, 0.5f, 0.5f);
		GpuRig.Run(Device, enc => {
			enc.BeginColorPass(unorm.DefaultView, ColorAttachmentOps.Clear(half)).Dispose();
			enc.BeginColorPass(srgb.DefaultView, ColorAttachmentOps.Clear(half)).Dispose();
		});
		RawColor32 u = GpuRig.At(GpuRig.Read(Device, unorm), 4, 0, 0);
		RawColor32 s = GpuRig.At(GpuRig.Read(Device, srgb), 4, 0, 0);
		Assert.InRange(u.R, 127, 128);
		// linear 0.5 is about 0.7354 sRGB-encoded; alpha isn't encoded
		Assert.InRange(s.R, 187, 188);
		Assert.InRange(s.A, 127, 128);
	}
}
