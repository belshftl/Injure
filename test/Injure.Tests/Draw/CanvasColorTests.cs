// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Draw;
using Injure.Gpu;
using Injure.Primitives;
using Injure.Tests.Gpu;

namespace Injure.Tests.Draw;

// Canvas works on sRGB-encoded values, so they have to reach a non-sRGB target unchanged
public sealed class CanvasColorTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private static readonly SrgbStraight32 clear = new(30, 30, 40);
	private static readonly SrgbStraight32 fill = new(80, 200, 120);

	private static CanvasParams baseParams() => CanvasParams.Primary(Viewport.Whole(new Vector2(4, 4)), clear);

	[Fact]
	public void CanvasWritesSrgbValuesUnchanged() {
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			using (OwnedCanvas canvas = new(frame, baseParams()))
				canvas.Canvas.Rect(new RectF(0, 0, 2, 4), fill);
			frame.Submit();
		}
		byte[] texels = GpuRig.Read(Device, target);
		Assert.Equal(fill.ToRaw(), GpuRig.At(texels, 4, 0, 0));
		Assert.Equal(clear.ToRaw(), GpuRig.At(texels, 4, 3, 3));
	}

	[Fact]
	public void CanvasRejectsSrgbTargets() {
		using GpuTexture target = GpuRig.Target(Device, 4, 4, format: TextureFormat.Rgba8UnormSrgb);
		using RenderFrame frame = new(Device, new GpuRig.FakeOutput(target));
		Assert.Throws<ArgumentException>(() => new OwnedCanvas(frame, baseParams()));
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
