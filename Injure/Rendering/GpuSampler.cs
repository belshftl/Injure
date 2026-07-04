// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for sampler wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GpuSamplerHandle {
	internal abstract WGPUSampler WgpuSampler { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUSampler"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUSampler DangerousGetNative() => WgpuSampler;
}

/// <summary>
/// Owning wrapper around a sampler.
/// </summary>
public sealed class GpuSampler : GpuSamplerHandle, IDisposable {
	private WGPUSampler sampler;

	internal GpuSampler(WGPUSampler sampler) {
		this.sampler = sampler;
	}

	internal override WGPUSampler WgpuSampler => sampler;

	/// <summary>
	/// Releases the underlying WebGPU sampler object.
	/// </summary>
	public void Dispose() {
		if (sampler.IsNotNull)
			wgpuSamplerRelease(sampler);
		sampler = default;
	}
}

/// <summary>
/// Non-owning wrapper around a sampler.
/// </summary>
public sealed class GpuSamplerRef : GpuSamplerHandle {
	private readonly GpuSampler source;
	internal GpuSamplerRef(GpuSampler source) {
		this.source = source;
	}

	internal override WGPUSampler WgpuSampler => source.WgpuSampler;
}

/// <summary>
/// Parameters used to create a <see cref="GpuSampler"/>.
/// </summary>
/// <param name="AddressModeU">Address mode for the U axis.</param>
/// <param name="AddressModeV">Address mode for the V axis.</param>
/// <param name="AddressModeW">Address mode for the W axis, if applicable.</param>
/// <param name="MagFilter">Magnification filter (behavior when the sampled area is smaller than or equal to one texel).</param>
/// <param name="MinFilter">Minification filter (behavior when the sampled area is larger than one texel).</param>
/// <param name="MipmapFilter">Mipmap filter (behavior when sampling between mipmap levels).</param>
/// <param name="LodMinClamp">Minimum level of detail used internally when sampling a texture.</param>
/// <param name="LodMaxClamp">Maximum level of detail used internally when sampling a texture.</param>
/// <param name="Compare">If provided, the sampler will be a comparison sampler using the specified function.</param>
/// <param name="MaxAnisotropy">Maximum anisotropy clamp value.</param>
/// <remarks>
/// <paramref name="MagFilter"/>, <paramref name="MinFilter"/>, and <paramref name="MipmapFilter"/> must
/// all be set to <see cref="FilterMode.Linear"/> (or <see cref="MipmapFilterMode.Linear"/>) if
/// <paramref name="MaxAnisotropy"/> is larger than 1.
/// </remarks>
public readonly record struct GpuSamplerCreateParams(
	AddressMode AddressModeU,
	AddressMode AddressModeV,
	AddressMode AddressModeW,
	FilterMode MagFilter,
	FilterMode MinFilter,
	MipmapFilterMode MipmapFilter,
	float LodMinClamp = 0f,
	float LodMaxClamp = 32f,
	CompareFunction Compare = default,
	ushort MaxAnisotropy = 1
);
