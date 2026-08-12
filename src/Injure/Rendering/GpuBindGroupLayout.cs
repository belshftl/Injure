// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for bind group layout wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GpuBindGroupLayoutHandle {
	internal abstract WGPUBindGroupLayout WgpuBindGroupLayout { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUBindGroupLayout"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUBindGroupLayout DangerousGetNative() => WgpuBindGroupLayout;
}

/// <summary>
/// Owning wrapper around a bind group layout.
/// </summary>
public sealed class GpuBindGroupLayout : GpuBindGroupLayoutHandle, IDisposable {
	private WGPUBindGroupLayout bindGroupLayout;

	internal GpuBindGroupLayout(WGPUBindGroupLayout bindGroupLayout) {
		this.bindGroupLayout = bindGroupLayout;
	}

	internal override WGPUBindGroupLayout WgpuBindGroupLayout => bindGroupLayout;

	/// <summary>
	/// Creates a non-owning view of this bind group layout.
	/// </summary>
	public GpuBindGroupLayoutRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU bind group layout.
	/// </summary>
	public void Dispose() {
		if (bindGroupLayout.IsNotNull)
			wgpuBindGroupLayoutRelease(bindGroupLayout);
		bindGroupLayout = default;
	}
}

/// <summary>
/// Non-owning wrapper around a bind group layout.
/// </summary>
public sealed class GpuBindGroupLayoutRef : GpuBindGroupLayoutHandle {
	private readonly GpuBindGroupLayout source;
	internal GpuBindGroupLayoutRef(GpuBindGroupLayout source) {
		this.source = source;
	}

	internal override WGPUBindGroupLayout WgpuBindGroupLayout => source.WgpuBindGroupLayout;
}
