// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for pipeline layout wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GpuPipelineLayoutHandle {
	internal abstract WGPUPipelineLayout WgpuPipelineLayout { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUPipelineLayout"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUPipelineLayout DangerousGetNative() => WgpuPipelineLayout;
}

/// <summary>
/// Owning wrapper around a pipeline layout.
/// </summary>
public sealed class GpuPipelineLayout : GpuPipelineLayoutHandle, IDisposable {
	private WGPUPipelineLayout pipelineLayout;

	internal GpuPipelineLayout(WGPUPipelineLayout pipelineLayout) {
		this.pipelineLayout = pipelineLayout;
	}

	internal override WGPUPipelineLayout WgpuPipelineLayout => pipelineLayout;

	/// <summary>
	/// Creates a non-owning view of this pipeline layout.
	/// </summary>
	public GpuPipelineLayoutRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU pipeline layout.
	/// </summary>
	public void Dispose() {
		if (pipelineLayout.IsNotNull)
			wgpuPipelineLayoutRelease(pipelineLayout);
		pipelineLayout = default;
	}
}

/// <summary>
/// Non-owning wrapper around a pipeline layout.
/// </summary>
public sealed class GpuPipelineLayoutRef : GpuPipelineLayoutHandle {
	private readonly GpuPipelineLayout source;
	internal GpuPipelineLayoutRef(GpuPipelineLayout source) {
		this.source = source;
	}

	internal override WGPUPipelineLayout WgpuPipelineLayout => source.WgpuPipelineLayout;
}
