// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Tests.Gpu;

// our own preliminary checks, rather than WebGPU validation errors
public sealed class GpuValidationTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	// ==========================================================================
	// queue writes
	[Fact]
	public void WriteToBufferValidatesArguments() {
		using GpuBuffer buf = Device.CreateBuffer(16, BufferUsage.CopyDst);
		using GpuBuffer noCopyDst = Device.CreateBuffer(16, BufferUsage.Uniform);
		Assert.Throws<ArgumentNullException>(() => Device.WriteToBuffer(null!, 0, 1u));
		Assert.Throws<ArgumentException>(() => Device.WriteToBuffer(noCopyDst, 0, 1u));
		Assert.Throws<ArgumentException>(() => Device.WriteToBuffer(buf, 2, 1u));
		Assert.Throws<ArgumentException>(() => Device.WriteToBuffer(buf, 0, (ushort)1));
		Assert.Throws<ArgumentException>(() => Device.WriteToBuffer(buf, 0, new byte[6].AsSpan()));
		Assert.Throws<ArgumentException>(() => Device.WriteToBuffer(buf, 12, new uint[2].AsSpan()));
		Assert.Throws<ArgumentException>(() => Device.WriteToBuffer(buf, 20, 1u));
		Device.WriteToBuffer(buf, 12, 1u);
		Device.WriteToBuffer(buf, 0, ReadOnlySpan<byte>.Empty);
	}

	[Fact]
	public void WriteToTextureValidatesArguments() {
		using GpuTexture tex = Device.CreateTexture(new GpuTextureCreateParams(
			4, 4, 1, 2, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.CopyDst
		));
		using GpuTexture noCopyDst = GpuRig.Target(Device, 4, 4);
		byte[] data = new byte[64];
		GpuTextureLayout layout = new(0, 16, 4);
		Assert.Throws<ArgumentException>(() => Device.WriteToTexture(noCopyDst, GpuRig.Whole(noCopyDst), data.AsSpan(), layout));
		Assert.Throws<ArgumentException>(() => Device.WriteToTexture(tex, new GpuTextureRegion(0, 0, 0, 4, 4, 1, 2, TextureAspect.All), data.AsSpan(), layout));
		// mip 1 is 2x2
		Assert.Throws<ArgumentException>(() => Device.WriteToTexture(tex, new GpuTextureRegion(0, 0, 0, 4, 4, 1, 1, TextureAspect.All), data.AsSpan(), layout));
		Assert.Throws<ArgumentException>(() => Device.WriteToTexture(tex, new GpuTextureRegion(2, 0, 0, 4, 4, 1, 0, TextureAspect.All), data.AsSpan(), layout));
		Device.WriteToTexture(tex, new GpuTextureRegion(0, 0, 0, 2, 2, 1, 1, TextureAspect.All), data.AsSpan(), new GpuTextureLayout(0, 8, 2));
	}

	[Fact]
	public void TextureCopiesCheckRegionBounds() {
		using GpuTexture a = GpuRig.Target(Device, 4, 4, extraUsage: TextureUsage.CopyDst);
		using GpuTexture b = GpuRig.Target(Device, 4, 4, extraUsage: TextureUsage.CopyDst);
		using GpuBuffer buf = Device.CreateBuffer(1024 * 4, BufferUsage.CopySrc | BufferUsage.CopyDst);
		using GpuCommandEncoder enc = Device.CreateCommandEncoder();
		GpuTextureRegion tooWide = new(1, 0, 0, 4, 4, 1, 0, TextureAspect.All);
		GpuTextureRegion noSuchMip = new(0, 0, 0, 1, 1, 1, 1, TextureAspect.All);
		Assert.Throws<ArgumentException>(() => enc.CopyTextureToBuffer(a, tooWide, buf, new GpuTextureLayout(0, 256, 4)));
		Assert.Throws<ArgumentException>(() => enc.CopyBufferToTexture(buf, new GpuTextureLayout(0, 256, 4), a, tooWide));
		Assert.Throws<ArgumentException>(() => enc.CopyTextureToTexture(a, GpuRig.Whole(a), b, tooWide));
		Assert.Throws<ArgumentException>(() => enc.CopyTextureToBuffer(a, noSuchMip, buf, new GpuTextureLayout(0, 256, 1)));
	}

	// ==========================================================================
	// render pass state
	private void inPass(uint size, Action<RenderPass> body) {
		using GpuTexture target = GpuRig.Target(Device, size, size);
		using GpuCommandEncoder enc = Device.CreateCommandEncoder();
		using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Load);
		body(pass);
	}

	[Fact]
	public void VertexAndIndexBuffersAreValidated() {
		using GpuBuffer vb = Device.CreateBuffer(64, BufferUsage.Vertex);
		using GpuBuffer ib = Device.CreateBuffer(64, BufferUsage.Index);
		inPass(4, pass => {
			Assert.Throws<ArgumentException>(() => pass.SetVertexBuffer(0, ib));
			Assert.Throws<ArgumentException>(() => pass.SetVertexBuffer(0, vb, 2));
			Assert.Throws<ArgumentException>(() => pass.SetVertexBuffer(0, vb, 32, 64));
			Assert.Throws<ArgumentException>(() => pass.SetVertexBuffer(0, vb, 68));
			pass.SetVertexBuffer(0, vb, 32, 32);

			Assert.Throws<ArgumentException>(() => pass.SetIndexBuffer(vb, IndexFormat.Uint16));
			Assert.Throws<ArgumentException>(() => pass.SetIndexBuffer(ib, IndexFormat.Undefined));
			Assert.Throws<ArgumentException>(() => pass.SetIndexBuffer(ib, IndexFormat.Uint32, 2));
			Assert.Throws<ArgumentException>(() => pass.SetIndexBuffer(ib, IndexFormat.Uint16, 1));
			Assert.Throws<ArgumentException>(() => pass.SetIndexBuffer(ib, IndexFormat.Uint16, 32, 34));
			pass.SetIndexBuffer(ib, IndexFormat.Uint16, 2, 32);
		});
	}

	[Fact]
	public void DynamicOffsetsAreValidated() {
		using GpuRig.Tinted tint = new(Device, Color32.Red, Color32.White);
		using GpuBindGroupLayout plainLayout = Device.CreateUniformBufferBindGroupLayout(ShaderStage.Fragment);
		using GpuBindGroup plain = Device.CreateUniformBufferBindGroup(plainLayout, tint.Uniforms, size: 16);
		inPass(4, pass => {
			Assert.Throws<ArgumentException>(() => pass.SetBindGroup(0, tint.Group));
			Assert.Throws<ArgumentException>(() => pass.SetBindGroup(0, tint.Group, [0, 0]));
			Assert.Throws<ArgumentException>(() => pass.SetBindGroup(0, tint.Group, [tint.Stride / 2]));
			Assert.Throws<ArgumentException>(() => pass.SetBindGroup(0, plain, [0]));
			pass.SetBindGroup(0, tint.Group, [tint.Stride]);
			pass.SetBindGroup(0, plain);
			// works through a ref too
			pass.SetBindGroup(0, tint.Group.AsRef(), [0]);
		});
	}

	[Fact]
	public void ScissorMustLieInTheAttachment() {
		inPass(8, pass => {
			Assert.Throws<ArgumentException>(() => pass.SetScissorRect(4, 0, 5, 8));
			Assert.Throws<ArgumentException>(() => pass.SetScissorRect(0, 0, 8, 9));
			Assert.Throws<ArgumentException>(() => pass.SetScissorRect(uint.MaxValue, 0, 2, 2));
			pass.SetScissorRect(4, 4, 4, 4);
			pass.SetScissorRect(0, 0, 0, 0);
		});
	}

	[Fact]
	public void ViewportFollowsWebgpuRules() {
		float max = Device.Limits.MaxTextureDimension2d;
		inPass(8, pass => {
			Assert.Throws<ArgumentException>(() => pass.SetViewport(0, 0, -1, 8));
			Assert.Throws<ArgumentException>(() => pass.SetViewport(0, 0, max + 1, 8));
			Assert.Throws<ArgumentException>(() => pass.SetViewport(float.NaN, 0, 8, 8));
			Assert.Throws<ArgumentException>(() => pass.SetViewport(-3 * max, 0, 8, 8));
			Assert.Throws<ArgumentException>(() => pass.SetViewport(0, 0, 8, 8, 0.5f, 0.25f));
			Assert.Throws<ArgumentException>(() => pass.SetViewport(0, 0, 8, 8, 0f, 1.5f));
			// may extend past the attachment
			pass.SetViewport(-4, -4, 16, 16);
			pass.SetViewport(0, 0, 8, 8, 0.25f, 0.25f);
		});
	}

	[Fact]
	public void DrawsRequireTheirState() {
		using GpuRig.Tinted tint = new(Device, Color32.Red);
		using GpuRenderPipeline p = tint.Pipeline(Device, [GpuRig.Opaque()]);
		using GpuBuffer args = Device.CreateBuffer(32, BufferUsage.Indirect);
		inPass(4, pass => {
			Assert.Throws<InvalidOperationException>(() => pass.Draw(3));
			Assert.Throws<InvalidOperationException>(() => pass.DrawIndirect(args, 0));
			pass.SetPipeline(p);
			Assert.Throws<InvalidOperationException>(() => pass.DrawIndexed(3));
			Assert.Throws<InvalidOperationException>(() => pass.DrawIndexedIndirect(args, 0));
			Assert.Throws<ArgumentException>(() => pass.DrawIndirect(args, 20));
			Assert.Throws<ArgumentException>(() => pass.DrawIndexedIndirect(args, 16));
		});
	}
}
