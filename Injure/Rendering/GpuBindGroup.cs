// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for bind group wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GpuBindGroupHandle {
	internal abstract WGPUBindGroup WgpuBindGroup { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUBindGroup"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUBindGroup DangerousGetNative() => WgpuBindGroup;
}

/// <summary>
/// Owning wrapper around a bind group .
/// </summary>
public sealed class GpuBindGroup : GpuBindGroupHandle, IDisposable {
	private WGPUBindGroup bindGroup;

	internal GpuBindGroup(WGPUBindGroup bindGroup) {
		this.bindGroup = bindGroup;
	}

	internal override WGPUBindGroup WgpuBindGroup => bindGroup;

	/// <summary>
	/// Creates a non-owning view of this bind group.
	/// </summary>
	public GpuBindGroupRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU bind group.
	/// </summary>
	public void Dispose() {
		if (bindGroup.IsNotNull)
			wgpuBindGroupRelease(bindGroup);
		bindGroup = default;
	}
}

/// <summary>
/// Non-owning wrapper around a bind group.
/// </summary>
public sealed class GpuBindGroupRef : GpuBindGroupHandle {
	private readonly GpuBindGroup source;
	internal GpuBindGroupRef(GpuBindGroup source) {
		this.source = source;
	}

	internal override WGPUBindGroup WgpuBindGroup => source.WgpuBindGroup;
}
