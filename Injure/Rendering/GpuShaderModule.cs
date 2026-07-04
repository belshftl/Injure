// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for shader module wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GpuShaderModuleHandle {
	internal abstract WGPUShaderModule WgpuShaderModule { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUShaderModule"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUShaderModule DangerousGetNative() => WgpuShaderModule;
}

/// <summary>
/// Owning wrapper around a shader module.
/// </summary>
public sealed class GpuShaderModule : GpuShaderModuleHandle, IDisposable {
	private WGPUShaderModule shaderModule;

	internal GpuShaderModule(WGPUShaderModule shaderModule) {
		this.shaderModule = shaderModule;
	}

	internal override WGPUShaderModule WgpuShaderModule => shaderModule;

	/// <summary>
	/// Creates a non-owning view of this shader module.
	/// </summary>
	public GpuShaderModuleRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU shader module.
	/// </summary>
	public void Dispose() {
		if (shaderModule.IsNotNull)
			wgpuShaderModuleRelease(shaderModule);
		shaderModule = default;
	}
}

/// <summary>
/// Non-owning wrapper around a shader module.
/// </summary>
public sealed class GpuShaderModuleRef : GpuShaderModuleHandle {
	private readonly GpuShaderModule source;
	internal GpuShaderModuleRef(GpuShaderModule source) {
		this.source = source;
	}

	internal override WGPUShaderModule WgpuShaderModule => source.WgpuShaderModule;
}
