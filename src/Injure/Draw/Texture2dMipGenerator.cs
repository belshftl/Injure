// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// Renders the mip chains of <see cref="Texture2d"/>s, each level from the one above it.
/// Attached to the device, so there's one per <see cref="GpuDevice"/>; obtained with
/// <see cref="For(GpuDevice)"/>.
/// </summary>
internal sealed class Texture2dMipGenerator : IDisposable {
	private readonly GpuDevice device;
	private readonly GpuShaderModule shader;
	private readonly GpuPipelineLayout pipelineLayout;
	private readonly Lock pipelinesLock = new();
	private readonly Dictionary<(TextureFormat, Texture2dMipFilter), GpuRenderPipeline> pipelines = new();

	public static Texture2dMipGenerator For(GpuDevice device) => device.GetOrAttach(static d => new Texture2dMipGenerator(d));

	private Texture2dMipGenerator(GpuDevice device) {
		this.device = device;
		shader = device.CreateShaderModuleWgsl(BuiltinShaderSource.Load("mipGen.wgsl"));
		pipelineLayout = device.CreatePipelineLayout([DrawDeviceResources.For(device).ColorTexture2dViewLayout]);
	}

	/// <summary>
	/// Renders every level of <paramref name="texture"/> below the base one.
	/// </summary>
	/// <remarks>
	/// Submitted right away, so the work is ordered after earlier queue writes to the texture and
	/// before any command buffer submitted later, such as the current frame's.
	/// </remarks>
	public void Generate(GpuTexture texture, Texture2dMipFilter filter) {
		if (texture.MipLevelCount <= 1)
			throw new InternalStateException("can't generate mips for a texture with only 1 mip level (or lower)");
		GpuRenderPipeline pipeline = getPipeline(texture.Format, filter);
		var draw = DrawDeviceResources.For(device);
		List<IDisposable> tmp = new();
		try {
			using GpuCommandEncoder enc = device.CreateCommandEncoder();
			GpuTextureView src = level(texture, 0);
			tmp.Add(src);
			for (uint i = 1; i < texture.MipLevelCount; i++) {
				GpuTextureView dst = level(texture, i);
				tmp.Add(dst);
				GpuBindGroup group = draw.CreateColorTexture2dViewBindGroup(src);
				tmp.Add(group);
				// every texel gets written, so the old contents don't need loading
				using (RenderPass pass = enc.BeginColorPass(dst, ColorAttachmentOps.Clear(default))) {
					pass.SetPipeline(pipeline);
					pass.SetBindGroup(0, group);
					pass.Draw(3);
				}
				src = dst;
			}
			using GpuCommandBuffer cmdbuf = enc.Finish();
			device.Submit(cmdbuf);
		} finally {
			foreach (IDisposable d in tmp)
				d.Dispose();
		}
	}

	private static GpuTextureView level(GpuTexture texture, uint mip) =>
		texture.CreateView(new GpuTextureViewParams(Format: null, Dimension: null, Aspect: TextureAspect.All, BaseMipLevel: mip, MipLevelCount: 1));

	private GpuRenderPipeline getPipeline(TextureFormat format, Texture2dMipFilter filter) {
		lock (pipelinesLock) {
			if (pipelines.TryGetValue((format, filter), out GpuRenderPipeline? existing))
				return existing;
			GpuRenderPipeline created = device.CreateRenderPipeline(new GpuRenderPipelineParams(
				Layout: pipelineLayout,
				Vertex: new VertexState(ShaderModule: shader, EntryPoint: "vs_main"),
				Fragment: new FragmentState(
					ShaderModule: shader,
					EntryPoint: filter.Tag switch {
						Texture2dMipFilter.Case.Color => "fs_color",
						Texture2dMipFilter.Case.Raw => "fs_raw",
						_ => throw new UnreachableException(),
					},
					Targets: [new ColorTargetState(Format: format, Blend: null, WriteMask: ColorWriteMask.All)]
				),
				Primitive: new PrimitiveState(PrimitiveTopology.TriangleList)
			));
			pipelines.Add((format, filter), created);
			return created;
		}
	}

	public void Dispose() {
		lock (pipelinesLock) {
			foreach (GpuRenderPipeline p in pipelines.Values)
				p.Dispose();
			pipelines.Clear();
		}
		pipelineLayout.Dispose();
		shader.Dispose();
	}
}
