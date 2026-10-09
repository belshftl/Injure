// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Draw;
using Injure.Gpu;
using Injure.Primitives;
using Injure.Tests.Gpu;

namespace Injure.Tests.Draw;

// Canvas works on sRGB-encoded values, so they have to reach a non-sRGB target unchanged
public sealed class CanvasColorTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private static readonly SrgbColor32 clear = new(30, 30, 40);
	private static readonly SrgbColor32 fill = new(80, 200, 120);

	private CanvasSharedResources createResources() {
		EngineResourceStore engineResources = new();
		engineResources.RegisterSource(new EmbeddedEngineResourceSource(typeof(Canvas).Assembly, [
			BuiltinShaders.Primitive2d.ResourceId,
			BuiltinShaders.Textured2dColor.ResourceId,
			BuiltinShaders.Textured2dRmask.ResourceId,
			BuiltinShaders.Textured2dSdf.ResourceId,
		]));
		return new CanvasSharedResources(Device, engineResources);
	}

	private static CanvasParams baseParams() => new(
		Target: CanvasTarget.Primary,
		ColorAttachmentOps: ColorAttachmentOps.Clear(clear.ToRawF128()),
		Scissor: CanvasScissor.None,
		Transform: Matrix3x2.Identity,
		OutputState: CanvasOutputStates.Alpha,
		Material: CanvasMaterials.Color
	);

	[Fact]
	public void CanvasWritesSrgbValuesUnchanged() {
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using CanvasSharedResources resources = createResources();
		using ViewGlobals globals = new(Device, 4, 4);
		using (RenderFrame frame = new(Device, new GpuRig.FakeOutput(target))) {
			using (Canvas cv = new(Device, globals, frame, resources, baseParams()))
				cv.Rect(new RectF(0, 0, 2, 4), fill);
			frame.Submit();
		}
		byte[] texels = GpuRig.Read(Device, target);
		Assert.Equal(fill.ToRaw(), GpuRig.At(texels, 4, 0, 0));
		Assert.Equal(clear.ToRaw(), GpuRig.At(texels, 4, 3, 3));
	}

	[Fact]
	public void CanvasRejectsSrgbTargets() {
		using GpuTexture target = GpuRig.Target(Device, 4, 4, format: TextureFormat.Rgba8UnormSrgb);
		using CanvasSharedResources resources = createResources();
		using ViewGlobals globals = new(Device, 4, 4);
		using RenderFrame frame = new(Device, new GpuRig.FakeOutput(target));
		Assert.Throws<ArgumentException>(() => new Canvas(Device, globals, frame, resources, baseParams()));
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
