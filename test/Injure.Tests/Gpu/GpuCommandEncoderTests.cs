// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Tests.Gpu;

public sealed class GpuCommandEncoderTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private GpuDevice dev => Device;

	private uint[] readBack(GpuBufferHandle src, int count) {
		using GpuBuffer rb = dev.CreateBuffer((ulong)count * 4, BufferUsage.MapRead | BufferUsage.CopyDst);
		GpuRig.Run(dev, enc => enc.CopyBufferToBuffer(src, 0, rb, 0, (ulong)count * 4));
		rb.Map(MapMode.Read);
		uint[] vals = new uint[count];
		rb.ReadMapped(0, vals.AsSpan());
		rb.Unmap();
		return vals;
	}

	// ==========================================================================
	// lifecycle and submission
	[Fact]
	public void FinishConsumesTheEncoderAndSubmitConsumesTheBuffer() {
		GpuCommandEncoder enc = dev.CreateCommandEncoder();
		GpuCommandEncoderRef r = enc.AsRef();
		GpuCommandBuffer cmd = enc.Finish();
		Assert.Throws<InvalidOperationException>(enc.Finish);
		Assert.Throws<InvalidOperationException>(() => r.PushDebugGroup("x"));
		enc.Dispose();

		Assert.False(cmd.IsConsumed);
		dev.Submit(cmd);
		Assert.True(cmd.IsConsumed);
		Assert.Throws<ArgumentException>(() => dev.Submit(cmd));
		cmd.Dispose();
	}

	[Fact]
	public void DisposedEncoderAndBufferCantBeUsed() {
		GpuCommandEncoder enc = dev.CreateCommandEncoder();
		enc.Dispose();
		Assert.Throws<InvalidOperationException>(enc.Finish);
		Assert.Throws<InvalidOperationException>(() => enc.AsRef().InsertDebugMarker("x"));

		using GpuCommandEncoder enc2 = dev.CreateCommandEncoder();
		GpuCommandBuffer cmd = enc2.Finish();
		cmd.Dispose();
		Assert.True(cmd.IsConsumed);
		Assert.Throws<ArgumentException>(() => dev.Submit(cmd));
	}

	[Fact]
	public void SubmitSpanValidatesEveryElement() {
		using GpuCommandEncoder a = dev.CreateCommandEncoder(), b = dev.CreateCommandEncoder();
		GpuCommandBuffer ca = a.Finish(), cb = b.Finish();
		Assert.Throws<ArgumentException>(() => dev.Submit([ca, ca]));
		Assert.Throws<ArgumentNullException>(() => dev.Submit([ca, null!]));
		Assert.False(ca.IsConsumed);
		dev.Submit([ca, cb]);
		Assert.True(ca.IsConsumed && cb.IsConsumed);
	}

	[Fact]
	public void ActivePassBlocksOtherCommands() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		using GpuBuffer buf = dev.CreateBuffer(16, BufferUsage.CopyDst);
		GpuCommandEncoder enc = dev.CreateCommandEncoder();
		RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Load);
		Assert.Throws<InvalidOperationException>(() => enc.ClearBuffer(buf));
		Assert.Throws<InvalidOperationException>(() => enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Load));
		Assert.Throws<InvalidOperationException>(() => enc.AsRef().ClearBuffer(buf));
		Assert.Throws<InvalidOperationException>(enc.Finish);
		Assert.Throws<InvalidOperationException>(enc.Dispose);
		pass.Dispose();
		pass.Dispose();
		Assert.Throws<ObjectDisposedException>(() => pass.Draw(3));
		enc.ClearBuffer(buf);
		enc.Dispose();
	}

	[Fact]
	public void RefRecordsIntoTheOwningEncoder() {
		using GpuBuffer buf = GpuRig.BufferWith(dev, BufferUsage.CopySrc, new uint[] { 1, 2, 3, 4 }.AsSpan());
		using GpuCommandEncoder enc = dev.CreateCommandEncoder();
		GpuCommandEncoderRef r = enc.AsRef();
		r.PushDebugGroup("group");
		r.ClearBuffer(buf, 4, 8);
		r.PopDebugGroup();
		dev.Submit(enc.Finish());
		Assert.Equal(new uint[] { 1, 0, 0, 4 }, readBack(buf, 4));
	}

	// ==========================================================================
	// buffer commands
	[Fact]
	public void CopyBufferToBufferAndClear() {
		using GpuBuffer src = GpuRig.BufferWith(dev, BufferUsage.CopySrc, new uint[] { 1, 2, 3, 4 }.AsSpan());
		using GpuBuffer dst = dev.CreateBuffer(16, BufferUsage.CopySrc | BufferUsage.CopyDst);
		GpuRig.Run(dev, enc => {
			enc.CopyBufferToBuffer(src, 4, dst, 0, 12);
			enc.ClearBuffer(dst, 4, 4);
		});
		Assert.Equal(new uint[] { 2, 0, 4, 0 }, readBack(dst, 4));
	}

	[Fact]
	public void BufferCommandsValidateArguments() {
		using GpuBuffer src = dev.CreateBuffer(16, BufferUsage.CopySrc);
		using GpuBuffer dst = dev.CreateBuffer(16, BufferUsage.CopyDst);
		using GpuBuffer both = dev.CreateBuffer(16, BufferUsage.CopySrc | BufferUsage.CopyDst);
		using GpuCommandEncoder enc = dev.CreateCommandEncoder();
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToBuffer(dst, 0, dst, 0, 4));
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToBuffer(src, 0, src, 0, 4));
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToBuffer(both, 0, both, 8, 4));
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToBuffer(src, 2, dst, 0, 4));
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToBuffer(src, 0, dst, 0, 6));
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToBuffer(src, 8, dst, 0, 12));
		Assert.Throws<ArgumentNullException>(() => enc.CopyBufferToBuffer(null!, 0, dst, 0, 4));
		Assert.Throws<ArgumentException>(() => enc.ClearBuffer(src));
		Assert.Throws<ArgumentException>(() => enc.ClearBuffer(dst, 2));
		Assert.Throws<ArgumentException>(() => enc.ClearBuffer(dst, 8, 12));
		Assert.Throws<ArgumentException>(() => enc.ClearBuffer(dst, 20));
	}

	// ==========================================================================
	// texture copies
	[Fact]
	public void BufferToTextureToBufferRoundtrips() {
		using GpuTexture tex = dev.CreateTexture(new GpuTextureCreateParams(
			2, 2, 1, 1, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.CopySrc | TextureUsage.CopyDst
		));
		byte[] staged = new byte[256 + 8];
		for (int i = 0; i < 8; i++) {
			staged[i] = (byte)(i + 1);
			staged[256 + i] = (byte)(i + 11);
		}
		using GpuBuffer src = GpuRig.BufferWith(dev, BufferUsage.CopySrc, staged.AsSpan());
		GpuRig.Run(dev, enc => enc.CopyBufferToTexture(src, new GpuTextureLayout(0, 256, 2), tex, GpuRig.Whole(tex)));
		byte[] texels = GpuRig.Read(dev, tex);
		Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16, 17, 18 }, texels);
	}

	[Fact]
	public void TextureToTextureCopiesARegion() {
		using GpuTexture a = GpuRig.Target(dev, 4, 4);
		using GpuTexture b = GpuRig.Target(dev, 4, 4, extraUsage: TextureUsage.CopyDst);
		GpuRig.Run(dev, enc => {
			enc.BeginColorPass(a.DefaultView, ColorAttachmentOps.Clear(RawColor32.Red.ToRawF128())).Dispose();
			enc.BeginColorPass(b.DefaultView, ColorAttachmentOps.Clear(RawColor32.Black.ToRawF128())).Dispose();
			enc.CopyTextureToTexture(
				a, new GpuTextureRegion(0, 0, 0, 2, 2, 1, 0, TextureAspect.All),
				b, new GpuTextureRegion(2, 2, 0, 2, 2, 1, 0, TextureAspect.All)
			);
		});
		byte[] texels = GpuRig.Read(dev, b);
		Assert.Equal(RawColor32.Black, GpuRig.At(texels, 4, 1, 1));
		Assert.Equal(RawColor32.Red, GpuRig.At(texels, 4, 3, 3));
		Assert.Equal(RawColor32.Black, GpuRig.At(texels, 4, 1, 3));
	}

	[Fact]
	public void TextureCopiesValidateArguments() {
		using GpuTexture renderOnly = dev.CreateTexture(new GpuTextureCreateParams(
			4, 4, 1, 1, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.RenderAttachment
		));
		using GpuTexture copyable = GpuRig.Target(dev, 4, 4, extraUsage: TextureUsage.CopyDst);
		using GpuBuffer buf = dev.CreateBuffer(1024, BufferUsage.CopySrc | BufferUsage.CopyDst);
		using GpuCommandEncoder enc = dev.CreateCommandEncoder();
		GpuTextureRegion whole = GpuRig.Whole(copyable);
		Assert.Throws<ArgumentException>(() => enc.CopyTextureToBuffer(copyable, whole, buf, new GpuTextureLayout(0, 16, 4)));
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToTexture(buf, new GpuTextureLayout(0, 100, 4), copyable, whole));
		Assert.Throws<ArgumentException>(() => enc.CopyTextureToBuffer(renderOnly, whole, buf, new GpuTextureLayout(0, 256, 4)));
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToTexture(buf, new GpuTextureLayout(0, 256, 4), renderOnly, whole));
		Assert.Throws<ArgumentException>(
			() => enc.CopyTextureToTexture(
				copyable, whole, copyable,
				new GpuTextureRegion(0, 0, 0, 2, 2, 1, 0, TextureAspect.All)
			)
		);
	}
}
