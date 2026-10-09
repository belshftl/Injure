// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Numerics;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Tests.Gpu;

public sealed class GpuRenderTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private GpuDevice dev => Device;

	private static readonly Color32 green = Color32.Green;
	private static readonly Color32 blue = Color32.Blue;

	// draws a fullscreen triangle with the tint at uniform slot `slot` into `target`, after clearing it to blue
	private void drawFullscreen(GpuTexture target, GpuRig.Tinted tint, GpuRenderPipeline pipeline, uint slot,
		Action<RenderPass>? setup = null) {
		using GpuBuffer vb = GpuRig.BufferWith(dev, BufferUsage.Vertex, GpuRig.Fullscreen().AsSpan());
		GpuRig.Run(dev, enc => {
			using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Clear(blue));
			pass.SetPipeline(pipeline);
			pass.SetBindGroup(0, tint.Group, [slot * tint.Stride]);
			pass.SetVertexBuffer(0, vb);
			setup?.Invoke(pass);
			pass.Draw(3);
		});
	}

	// ==========================================================================
	// render pass validation
	[Fact]
	public void BeginRenderPassValidatesAttachments() {
		using GpuTexture a = GpuRig.Target(dev, 4, 4);
		using GpuTexture small = GpuRig.Target(dev, 2, 2);
		using GpuTexture ms = GpuRig.Target(dev, 4, 4, samples: 4);
		using GpuTexture sampledOnly = dev.CreateTexture(new GpuTextureCreateParams(
			4, 4, 1, 1, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.TextureBinding));
		using GpuTexture depth = GpuRig.Target(dev, 4, 4, format: TextureFormat.Depth32Float);
		using GpuTexture srgb = GpuRig.Target(dev, 4, 4, format: TextureFormat.Rgba8UnormSrgb);
		ColorAttachmentOps load = ColorAttachmentOps.Load;
		using GpuCommandEncoder enc = dev.CreateCommandEncoder();

		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([]));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass(new RenderPassColorAttachment[dev.Limits.MaxColorAttachments + 1]));
		Assert.Throws<ArgumentNullException>(() => enc.BeginRenderPass([new RenderPassColorAttachment(null!, load)]));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(sampledOnly.DefaultView, load)]));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(depth.DefaultView, load)]));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(a.DefaultView, load), new(small.DefaultView, load)]));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(a.DefaultView, load), new(ms.DefaultView, load)]));
		// resolve targets: only from multisampled, only into single-sampled of the same format and size
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(a.DefaultView, load, a.DefaultView)]));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(ms.DefaultView, load, ms.DefaultView)]));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(ms.DefaultView, load, srgb.DefaultView)]));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(ms.DefaultView, load, small.DefaultView)]));
		// depth/stencil: color formats rejected, ops must match the format's aspects
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([], new RenderPassDepthStencilAttachment(a.DefaultView)));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([], new RenderPassDepthStencilAttachment(
			depth.DefaultView, DepthAttachmentOps.Load, StencilAttachmentOps.Load)));
		Assert.Throws<ArgumentException>(() => enc.BeginRenderPass([new(small.DefaultView, load)],
			new RenderPassDepthStencilAttachment(depth.DefaultView, DepthAttachmentOps.Load)));
		Assert.Throws<ArgumentException>(() => enc.BeginColorDepthStencilPass(
			a.DefaultView, load, depth.DefaultView, DepthAttachmentOps.Load, StencilAttachmentOps.Load));

		// a depth-only pass is fine
		enc.BeginRenderPass([], new RenderPassDepthStencilAttachment(depth.DefaultView, DepthAttachmentOps.Clear(1f))).Dispose();
	}

	[Fact]
	public void ShorthandPassesRestrictDepthFormats() {
		using GpuTexture a = GpuRig.Target(dev, 4, 4);
		using GpuTexture ds = GpuRig.Target(dev, 4, 4, format: TextureFormat.Depth24PlusStencil8);
		using GpuCommandEncoder enc = dev.CreateCommandEncoder();
		Assert.Throws<ArgumentException>(() => enc.BeginColorDepthPass(a.DefaultView, ColorAttachmentOps.Load, ds.DefaultView,
			DepthAttachmentOps.Load));
	}

	[Fact]
	public void ClearColorReachesTheTarget() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		GpuRig.Run(dev, enc => enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Clear(new Color32(10, 20, 30, 40))).Dispose());
		Assert.Equal(new Color32(10, 20, 30, 40), GpuRig.At(GpuRig.Read(dev, target), 4, 2, 3));
	}

	// ==========================================================================
	// pipelines and bind groups
	[Fact]
	public void PipelineValidationWorks() {
		using GpuRig.Tinted tint = new(dev, green);
		Assert.Throws<ArgumentException>(() => tint.Pipeline(dev, ImmutableArray<ColorTargetState>.Empty));
		Assert.Throws<ArgumentException>(() => tint.Pipeline(dev, default));
		Assert.Throws<ArgumentException>(() => tint.Pipeline(dev, [GpuRig.Opaque()],
			primitive: new PrimitiveState(PrimitiveTopology.TriangleList, IndexFormat.Uint16, FrontFace.Ccw, CullMode.None, false)));
		using GpuRenderPipeline strip = tint.Pipeline(dev, [GpuRig.Opaque()], primitive: new PrimitiveState(PrimitiveTopology.TriangleStrip));
		Assert.Throws<ArgumentException>(() => dev.CreateRenderPipeline(new GpuRenderPipelineCreateParams(
			tint.PipelineLayout, new VertexState(tint.Shader, " "))));
	}

	[Fact]
	public void EmptyPipelineLayoutWorksForShadersWithoutBindings() {
		using GpuShaderModule sm = dev.CreateShaderModuleWgsl("""
			@vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
				return vec4f(f32(i & 1u) * 4.0 - 1.0, f32(i >> 1u) * 4.0 - 1.0, 0.0, 1.0);
			}
			@fragment fn fs() -> @location(0) vec4f { return vec4f(0.0, 1.0, 0.0, 1.0); }
			""");
		using GpuPipelineLayout pl = dev.CreatePipelineLayout([]);
		using GpuRenderPipeline p = dev.CreateRenderPipeline(new GpuRenderPipelineCreateParams(
			pl, new VertexState(sm, "vs"), new FragmentState(sm, "fs", [GpuRig.Opaque()])));
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		GpuRig.Run(dev, enc => {
			using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Clear(blue));
			pass.SetPipeline(p);
			pass.Draw(3);
		});
		Assert.Equal(green, GpuRig.At(GpuRig.Read(dev, target), 4, 0, 0));
	}

	[Fact]
	public void BindGroupValidationWorks() {
		using GpuBindGroupLayout layout = dev.CreateUniformBufferBindGroupLayout(ShaderStage.Fragment);
		using GpuBuffer ub = dev.CreateBuffer(64, BufferUsage.Uniform);
		Assert.Throws<ArgumentException>(() => dev.CreateBindGroupLayout([]));
		Assert.Throws<ArgumentException>(() => dev.CreateBindGroupLayout([
			new GpuBindGroupLayoutEntry(0, ShaderStage.Fragment, new GpuBufferBindingLayout(BufferBindingType.Uniform)),
			new GpuBindGroupLayoutEntry(0, ShaderStage.Fragment, new GpuSamplerBindingLayout(SamplerBindingType.Filtering)),
		]));
		Assert.Throws<ArgumentException>(() => dev.CreateBindGroup(layout, []));
		Assert.Throws<ArgumentException>(() => dev.CreateUniformBufferBindGroup(layout, ub, offset: 128));
		Assert.Throws<ArgumentException>(() => dev.CreateUniformBufferBindGroup(layout, ub, offset: 32, size: 64));
		Assert.Throws<ArgumentException>(() => dev.CreateUniformBufferBindGroup(layout, ub, size: 0));
		Assert.Throws<ArgumentNullException>(() => dev.CreateBindGroup(layout, [new GpuBindGroupEntry(0, null!)]));
	}

	// ==========================================================================
	// draws
	[Fact]
	public void DynamicOffsetSelectsTheUniform() {
		using GpuRig.Tinted tint = new(dev, Color32.Red, green);
		using GpuRenderPipeline p = tint.Pipeline(dev, [GpuRig.Opaque()]);
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		drawFullscreen(target, tint, p, 1);
		Assert.Equal(green, GpuRig.At(GpuRig.Read(dev, target), 4, 1, 1));
		drawFullscreen(target, tint, p, 0);
		Assert.Equal(Color32.Red, GpuRig.At(GpuRig.Read(dev, target), 4, 1, 1));
	}

	[Fact]
	public void IndexedAndIndirectDraws() {
		using GpuRig.Tinted tint = new(dev, green);
		using GpuRenderPipeline p = tint.Pipeline(dev, [GpuRig.Opaque()]);
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		using GpuBuffer ib = GpuRig.BufferWith(dev, BufferUsage.Index, new ushort[] { 2, 1, 0, 0 }.AsSpan());
		using GpuBuffer args = GpuRig.BufferWith(dev, BufferUsage.Indirect, new uint[] { 3, 1, 0, 0, 3, 1, 0, 0, 0 }.AsSpan());

		drawFullscreen(target, tint, p, 0, pass => {
			pass.SetIndexBuffer(ib, IndexFormat.Uint16);
			pass.DrawIndexed(3);
		});
		Assert.Equal(green, GpuRig.At(GpuRig.Read(dev, target), 4, 3, 3));
		drawFullscreen(target, tint, p, 0, pass => pass.DrawIndirect(args, 0));
		Assert.Equal(green, GpuRig.At(GpuRig.Read(dev, target), 4, 3, 3));
		drawFullscreen(target, tint, p, 0, pass => {
			pass.SetIndexBuffer(ib, IndexFormat.Uint16);
			pass.DrawIndexedIndirect(args, 16);
		});
		Assert.Equal(green, GpuRig.At(GpuRig.Read(dev, target), 4, 3, 3));

		using GpuCommandEncoder enc = dev.CreateCommandEncoder();
		using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Load);
		Assert.Throws<ArgumentException>(() => pass.DrawIndirect(ib, 0));
		Assert.Throws<ArgumentException>(() => pass.DrawIndirect(args, 2));
	}

	[Fact]
	public void ViewportAndScissorRestrictRendering() {
		using GpuRig.Tinted tint = new(dev, green);
		using GpuRenderPipeline p = tint.Pipeline(dev, [GpuRig.Opaque()]);
		using GpuTexture target = GpuRig.Target(dev, 8, 8);

		drawFullscreen(target, tint, p, 0, pass => pass.SetViewport(0, 0, 4, 8));
		byte[] texels = GpuRig.Read(dev, target);
		Assert.Equal(green, GpuRig.At(texels, 8, 1, 4));
		Assert.Equal(blue, GpuRig.At(texels, 8, 6, 4));

		drawFullscreen(target, tint, p, 0, pass => pass.SetScissorRect(0, 4, 8, 4));
		texels = GpuRig.Read(dev, target);
		Assert.Equal(blue, GpuRig.At(texels, 8, 4, 1));
		Assert.Equal(green, GpuRig.At(texels, 8, 4, 6));
	}

	[Fact]
	public void BlendConstantIsUsedByConstantFactors() {
		using GpuRig.Tinted tint = new(dev, Color32.White);
		BlendComponent useConstant = new(BlendOperation.Add, BlendFactor.Constant, BlendFactor.Zero);
		using GpuRenderPipeline p = tint.Pipeline(dev, [new ColorTargetState(GpuRig.Format, new BlendState(useConstant, useConstant), ColorWriteMask.All)]);
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		drawFullscreen(target, tint, p, 0, pass => pass.SetBlendConstant(new Vector4(1f, 0f, 1f, 1f)));
		Assert.Equal(new Color32(255, 0, 255, 255), GpuRig.At(GpuRig.Read(dev, target), 4, 1, 1));
	}

	[Fact]
	public void MultisampleResolveAndMultipleTargets() {
		using GpuRig.Tinted tint = new(dev, new Color32(255, 128, 0, 255));
		using GpuRenderPipeline p = tint.Pipeline(dev, [GpuRig.Opaque(), GpuRig.Opaque()], samples: 4, fragment: "fs2");
		using GpuTexture msA = GpuRig.Target(dev, 4, 4, samples: 4), msB = GpuRig.Target(dev, 4, 4, samples: 4);
		using GpuTexture outA = GpuRig.Target(dev, 4, 4), outB = GpuRig.Target(dev, 4, 4);
		using GpuBuffer vb = GpuRig.BufferWith(dev, BufferUsage.Vertex, GpuRig.Fullscreen().AsSpan());
		GpuRig.Run(dev, enc => {
			using RenderPass pass = enc.BeginRenderPass([
				new RenderPassColorAttachment(msA.DefaultView, ColorAttachmentOps.Clear(blue), outA.DefaultView),
				new RenderPassColorAttachment(msB.DefaultView, ColorAttachmentOps.Clear(blue), outB.DefaultView),
			]);
			pass.SetPipeline(p);
			pass.SetBindGroup(0, tint.Group, [0]);
			pass.SetVertexBuffer(0, vb);
			pass.Draw(3);
		});
		// fs2 writes the tint to target 0 and its BGRA swizzle to target 1
		Assert.Equal(new Color32(255, 128, 0, 255), GpuRig.At(GpuRig.Read(dev, outA), 4, 2, 2));
		Assert.Equal(new Color32(0, 128, 255, 255), GpuRig.At(GpuRig.Read(dev, outB), 4, 2, 2));
	}

	[Fact]
	public void DepthTestKeepsTheNearerTriangle() {
		using GpuRig.Tinted tint = new(dev, Color32.Red, green);
		DepthStencilState depth = new(TextureFormat.Depth32Float, true, CompareFunction.Less, default, default);
		using GpuRenderPipeline p = tint.Pipeline(dev, [GpuRig.Opaque()], depthStencil: depth);
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		using GpuTexture depthTex = GpuRig.Target(dev, 4, 4, format: TextureFormat.Depth32Float);
		using GpuBuffer near = GpuRig.BufferWith(dev, BufferUsage.Vertex, GpuRig.Fullscreen(0.25f).AsSpan());
		using GpuBuffer far = GpuRig.BufferWith(dev, BufferUsage.Vertex, GpuRig.Fullscreen(0.75f).AsSpan());
		GpuRig.Run(dev, enc => {
			using RenderPass pass = enc.BeginColorDepthPass(
				target.DefaultView,
				ColorAttachmentOps.Clear(blue),
				depthTex.DefaultView,
				DepthAttachmentOps.Clear(1f)
			);
			pass.SetPipeline(p);
			pass.SetBindGroup(0, tint.Group, [0]);
			pass.SetVertexBuffer(0, near);
			pass.Draw(3);
			pass.SetBindGroup(0, tint.Group, [tint.Stride]);
			pass.SetVertexBuffer(0, far);
			pass.Draw(3);
		});
		Assert.Equal(Color32.Red, GpuRig.At(GpuRig.Read(dev, target), 4, 1, 1));
	}

	[Fact]
	public void StencilReferenceGatesDrawing() {
		using GpuRig.Tinted tint = new(dev, green);
		StencilFaceState equal = new(CompareFunction.Equal, StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Keep);
		DepthStencilState ds = new(TextureFormat.Depth24PlusStencil8, false, CompareFunction.Always, equal, equal);
		using GpuRenderPipeline p = tint.Pipeline(dev, [GpuRig.Opaque()], depthStencil: ds);
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		using GpuTexture dsTex = GpuRig.Target(dev, 4, 4, format: TextureFormat.Depth24PlusStencil8);
		using GpuBuffer vb = GpuRig.BufferWith(dev, BufferUsage.Vertex, GpuRig.Fullscreen().AsSpan());

		Color32 drawWith(uint reference) {
			GpuRig.Run(dev, enc => {
				using RenderPass pass = enc.BeginColorDepthStencilPass(
					target.DefaultView,
					ColorAttachmentOps.Clear(blue),
					dsTex.DefaultView,
					DepthAttachmentOps.Clear(1f),
					StencilAttachmentOps.Clear(1)
				);
				pass.SetPipeline(p);
				pass.SetBindGroup(0, tint.Group, [0]);
				pass.SetVertexBuffer(0, vb);
				pass.SetStencilReference(reference);
				pass.Draw(3);
			});
			return GpuRig.At(GpuRig.Read(dev, target), 4, 1, 1);
		}
		Assert.Equal(green, drawWith(1));
		Assert.Equal(blue, drawWith(0));
	}

	[Fact]
	public void SampledTextureBindGroupWorks() {
		// sample a 1x1 texture through a texture + sampler bind group
		using GpuTexture src = dev.CreateTexture(new GpuTextureCreateParams(
			1, 1, 1, 1, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.TextureBinding | TextureUsage.CopyDst
		));
		dev.WriteToTexture(src, GpuRig.Whole(src), new byte[] { 12, 34, 56, 255 }.AsSpan(), new GpuTextureLayout(0, 4, 1));
		using GpuSampler sampler = dev.CreateSampler(SamplerStates.NearestClamp);
		using GpuBindGroupLayout bgl = dev.CreateBindGroupLayout([
			new GpuBindGroupLayoutEntry(0, ShaderStage.Fragment, new GpuTextureBindingLayout(TextureSampleType.Float, TextureViewDimension.Dimension2d)),
			new GpuBindGroupLayoutEntry(1, ShaderStage.Fragment, new GpuSamplerBindingLayout(SamplerBindingType.Filtering)),
		]);
		using GpuBindGroup bg = dev.CreateBindGroup(bgl, [
			new GpuBindGroupEntry(0, new GpuTextureViewBindingResource(src.DefaultView)),
			new GpuBindGroupEntry(1, new GpuSamplerBindingResource(sampler)),
		]);
		using GpuShaderModule sm = dev.CreateShaderModuleWgsl("""
			@group(0) @binding(0) var tex: texture_2d<f32>;
			@group(0) @binding(1) var smp: sampler;
			@vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
				return vec4f(f32(i & 1u) * 4.0 - 1.0, f32(i >> 1u) * 4.0 - 1.0, 0.0, 1.0);
			}
			@fragment fn fs() -> @location(0) vec4f { return textureSample(tex, smp, vec2f(0.5, 0.5)); }
			""");
		using GpuPipelineLayout pl = dev.CreatePipelineLayout([bgl]);
		using GpuRenderPipeline p = dev.CreateRenderPipeline(new GpuRenderPipelineCreateParams(
			pl, new VertexState(sm, "vs"), new FragmentState(sm, "fs", [GpuRig.Opaque()])
		));
		using GpuTexture target = GpuRig.Target(dev, 2, 2);
		GpuRig.Run(dev, enc => {
			using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Clear(blue));
			pass.SetPipeline(p);
			pass.SetBindGroup(0, bg);
			pass.Draw(3);
		});
		Assert.Equal(new Color32(12, 34, 56, 255), GpuRig.At(GpuRig.Read(dev, target), 2, 1, 1));
	}
}
