// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for render pipeline wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GpuRenderPipelineHandle {
	internal abstract WGPURenderPipeline WgpuRenderPipeline { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPURenderPipeline"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPURenderPipeline DangerousGetNative() => WgpuRenderPipeline;
}

/// <summary>
/// Owning wrapper around a render pipeline.
/// </summary>
public sealed class GpuRenderPipeline : GpuRenderPipelineHandle, IDisposable {
	private WGPURenderPipeline renderPipeline;

	internal GpuRenderPipeline(WGPURenderPipeline renderPipeline) {
		this.renderPipeline = renderPipeline;
	}

	internal override WGPURenderPipeline WgpuRenderPipeline => renderPipeline;

	/// <summary>
	/// Creates a non-owning view of this render pipeline.
	/// </summary>
	public GpuRenderPipelineRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU render pipeline.
	/// </summary>
	public void Dispose() {
		if (renderPipeline.IsNotNull)
			wgpuRenderPipelineRelease(renderPipeline);
		renderPipeline = default;
	}
}

/// <summary>
/// Non-owning wrapper around a render pipeline.
/// </summary>
public sealed class GpuRenderPipelineRef : GpuRenderPipelineHandle {
	private readonly GpuRenderPipeline source;
	internal GpuRenderPipelineRef(GpuRenderPipeline source) {
		this.source = source;
	}

	internal override WGPURenderPipeline WgpuRenderPipeline => source.WgpuRenderPipeline;
}

/// <summary>
/// Parameters used to create a <see cref="GpuRenderPipeline"/>.
/// </summary>
/// <param name="Layout">Layout for this pipeline.</param>
/// <param name="Vertex">Vertex state.</param>
/// <param name="Fragment">
/// Fragment state; may be <see langword="null"/> for vertex-only pipelines.
/// </param>
/// <param name="Primitive">
/// Primitive state, or <see langword="null"/> for the default value of
/// <c>new PrimitiveState()</c>.
/// </param>
/// <param name="DepthStencil">
/// Depth/stencil state; may be <see langword="null"/> for color-only pipelines.
/// </param>
/// <param name="Multisample">
/// Multisample state, or <see langword="null"/> for the default value of
/// <c>new MultisampleState()</c>.
/// </param>
public readonly record struct GpuRenderPipelineCreateParams(
	GpuPipelineLayoutHandle Layout,
	VertexState Vertex,
	FragmentState? Fragment = null,
	PrimitiveState? Primitive = null,
	DepthStencilState? DepthStencil = null,
	MultisampleState? Multisample = null
);
