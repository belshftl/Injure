// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Common base type for bind group layout wrappers, allowing APIs to accept both owning and
/// non-owning wrappers.
/// </summary>
public abstract class GpuBindGroupLayoutHandle {
	internal abstract WGPUBindGroupLayout WgpuBindGroupLayout { get; }

	// the types of the buffer bindings declared with HasDynamicOffset, in binding order, i.e. the
	// order SetBindGroup expects their offsets in
	internal abstract BufferBindingType[] DynamicBindings { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUBindGroupLayout"/>, bypassing ownership/lifetime. Dangles
	/// once freed by <see cref="GpuBindGroupLayout.Dispose()"/>.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b> See
	/// <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public WGPUBindGroupLayout DangerousGetNative() => WgpuBindGroupLayout;
}

/// <summary>
/// Owning wrapper around a bind group layout.
/// </summary>
public sealed class GpuBindGroupLayout : GpuBindGroupLayoutHandle, IDisposable {
	private WGPUBindGroupLayout bindGroupLayout;

	internal GpuBindGroupLayout(WGPUBindGroupLayout bindGroupLayout, BufferBindingType[] dynamicBindings) {
		this.bindGroupLayout = bindGroupLayout;
		DynamicBindings = dynamicBindings;
	}

	internal override WGPUBindGroupLayout WgpuBindGroupLayout => bindGroupLayout;
	internal override BufferBindingType[] DynamicBindings { get; }

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
	internal override BufferBindingType[] DynamicBindings => source.DynamicBindings;
}
