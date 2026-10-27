// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Common base type for pipeline layout wrappers, allowing APIs to accept both owning and
/// non-owning wrappers.
/// </summary>
/// <remarks>
/// Once the owning object is disposed, anything that needs the underlying WebGPU object throws
/// <see cref="ObjectDisposedException"/>, whether it's reached through the owning object or a
/// non-owning wrapper, or passed to another API.
/// </remarks>
public abstract class GpuPipelineLayoutHandle {
	internal abstract WGPUPipelineLayout WgpuPipelineLayout { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUPipelineLayout"/>, bypassing ownership/lifetime. Dangles
	/// once freed by <see cref="GpuPipelineLayout.Dispose()"/>.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the pipeline layout has been disposed.
	/// </exception>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b> See
	/// <c>docs/conventions/dangerous-get-create.md</c>.
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

	internal override WGPUPipelineLayout WgpuPipelineLayout {
		get {
			ObjectDisposedException.ThrowIf(pipelineLayout.IsNull, this);
			return pipelineLayout;
		}
	}

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
