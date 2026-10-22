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
		using GpuTexture tex = Device.CreateTexture(new GpuTextureParams(
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
		using GpuRig.Tinted tint = new(Device, RawColor32.Red, RawColor32.White);
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
		using GpuRig.Tinted tint = new(Device, RawColor32.Red);
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

	// ==========================================================================
	// invalid default values
	private static void throwsDefault(string paramName, Action action) =>
		Assert.Equal(paramName, Assert.Throws<ArgumentInvalidDefaultException>(action).ParamName);

	[Fact]
	public void CreateMethodsRejectNestedDefaults() {
		throwsDefault("params", () => Device.CreateSampler(default));
		throwsDefault("params.MinFilter", () => Device.CreateSampler(SamplerStates.LinearClamp with { MinFilter = default }));
		throwsDefault("params.Dimension", () => Device.CreateTexture(new GpuTextureParams(
			4, 4, 1, 1, 1, default, GpuRig.Format, TextureUsage.TextureBinding
		)));

		using GpuTexture tex = GpuRig.Target(Device, 4, 4);
		throwsDefault("params", () => tex.CreateView(default));
		throwsDefault("params.Aspect", () => tex.CreateView(new GpuTextureViewParams(null, null, default, BaseMipLevel: 1)));
		throwsDefault("params.Dimension", () => tex.CreateView(new GpuTextureViewParams(null, default(TextureViewDimension), TextureAspect.All)));

		throwsDefault("entries[1]", () => Device.CreateBindGroupLayout([
			new GpuBindGroupLayoutEntry(0, ShaderStage.Vertex, new GpuBufferBindingLayout(BufferBindingType.Uniform)),
			default,
		]));
		throwsDefault("entries[0].Layout.Type", () => Device.CreateBindGroupLayout([
			new GpuBindGroupLayoutEntry(0, ShaderStage.Vertex, new GpuBufferBindingLayout(default)),
		]));

		// the default checks run before the null checks, so the pipelines don't need real shaders
		VertexState vert = new(null!, "vs");
		throwsDefault("params.Vertex.Buffers[0].Attributes[0].Format", () => Device.CreateRenderPipeline(new GpuRenderPipelineParams(
			null!, vert with { Buffers = [new VertexBufferLayout(4, VertexStepMode.Vertex, [new VertexAttribute(default, 0, 1)])] }
		)));
		throwsDefault("params.DepthStencil.StencilFront", () => Device.CreateRenderPipeline(new GpuRenderPipelineParams(
			null!, vert, DepthStencil: new DepthStencilState(TextureFormat.Depth32Float, true, CompareFunction.Less, default, StencilFaceState.Keep)
		)));
		throwsDefault("params.Primitive.CullMode", () => Device.CreateRenderPipeline(new GpuRenderPipelineParams(
			null!, vert, Primitive: new PrimitiveState() with { CullMode = default }
		)));
		BlendState badBlend = BlendStates.Alpha with { Alpha = BlendStates.Alpha.Alpha with { DstFactor = default } };
		throwsDefault("params.Fragment.Targets[0].Blend.Alpha.DstFactor", () => Device.CreateRenderPipeline(new GpuRenderPipelineParams(
			null!, vert, Fragment: new FragmentState(null!, "fs", [new ColorTargetState(GpuRig.Format, badBlend, ColorWriteMask.All)])
		)));
	}

	[Fact]
	public void CommandsRejectDefaults() {
		using GpuTexture tex = GpuRig.Target(Device, 4, 4, extraUsage: TextureUsage.CopyDst);
		using GpuBuffer buf = Device.CreateBuffer(1024 * 4, BufferUsage.CopySrc);
		using GpuBuffer mappable = Device.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		using GpuCommandEncoder enc = Device.CreateCommandEncoder();
		throwsDefault("colorOps", () => enc.BeginColorPass(tex.DefaultView, default));
		throwsDefault("colorAttachments[0].Ops", () => enc.BeginRenderPass([new RenderPassColorAttachment(tex.DefaultView, default)]));
		throwsDefault("srcLayout", () => enc.CopyBufferToTexture(buf, default, tex, GpuRig.Whole(tex)));
		throwsDefault("dstRegion.Aspect", () => enc.CopyBufferToTexture(
			buf, new GpuTextureLayout(0, 256, 4), tex, GpuRig.Whole(tex) with { Aspect = default }
		));
		throwsDefault("mode", () => mappable.Map(default));
	}

	// ==========================================================================
	// params properties
	[Fact]
	public void SamplerParamsRoundTrip() {
		GpuSamplerParams p = SamplerStates.LinearClamp with { MaxAnisotropy = 4, Compare = CompareFunction.Less };
		using GpuSampler sampler = Device.CreateSampler(p);
		Assert.Equal(p, sampler.Params);
		Assert.Equal(p, sampler.AsRef().Params);
		Assert.Equal(p.MinFilter, sampler.MinFilter);
		Assert.Equal((ushort)4, sampler.MaxAnisotropy);
		Assert.Equal(CompareFunction.Less, sampler.AsRef().Compare);
	}

	[Fact]
	public void TextureParamsHaveEmptyRatherThanDefaultViewFormats() {
		GpuTextureParams p = new(4, 2, 1, 1, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.TextureBinding);
		using GpuTexture tex = Device.CreateTexture(p);
		Assert.False(tex.Params.ViewFormats.IsDefault);
		Assert.Equal(p with { ViewFormats = [] }, tex.Params with { ViewFormats = [] });
		Assert.Equal((4u, 2u), (tex.AsRef().Width, tex.AsRef().Height));
		Assert.Equal(0, tex.ViewFormats.Length);
	}

	[Fact]
	public void ViewParamsAreResolved() {
		using GpuTexture tex = Device.CreateTexture(new GpuTextureParams(
			8, 8, 3, 2, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.TextureBinding
		));
		using GpuTextureView view = tex.CreateView(new GpuTextureViewParams(null, null, TextureAspect.All, BaseArrayLayer: 1));
		Assert.Equal(
			new GpuTextureViewParams(GpuRig.Format, TextureViewDimension.Dimension2dArray, TextureAspect.All, 0, 2, 1, 2),
			view.Params
		);
		Assert.Equal(tex.DefaultViewDimension, tex.DefaultView.Params.Dimension);
		using GpuTextureView again = tex.CreateView(view.Params);
		Assert.Equal(view.Params, again.Params);
		Assert.Equal((8u, 8u), (again.Width, again.Height));
	}
}
