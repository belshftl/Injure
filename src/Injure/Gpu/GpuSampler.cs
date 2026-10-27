// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Common base type for sampler wrappers, allowing APIs to accept both owning and non-owning
/// wrappers.
/// </summary>
/// <remarks>
/// Once the owning object is disposed, anything that needs the underlying WebGPU object throws
/// <see cref="ObjectDisposedException"/>, whether it's reached through the owning object or a
/// non-owning wrapper, or passed to another API.
/// </remarks>
public abstract class GpuSamplerHandle {
	internal abstract WGPUSampler WgpuSampler { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUSampler"/>, bypassing ownership/lifetime. Dangles once
	/// freed by <see cref="GpuSampler.Dispose()"/>.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the sampler has been disposed.
	/// </exception>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public WGPUSampler DangerousGetNative() => WgpuSampler;

	/// <summary>
	/// Parameters the sampler was created with.
	/// </summary>
	public abstract GpuSamplerParams Params { get; }

	/// <inheritdoc cref="GpuSamplerParams.AddressModeU"/>
	public AddressMode AddressModeU => Params.AddressModeU;

	/// <inheritdoc cref="GpuSamplerParams.AddressModeV"/>
	public AddressMode AddressModeV => Params.AddressModeV;

	/// <inheritdoc cref="GpuSamplerParams.AddressModeW"/>
	public AddressMode AddressModeW => Params.AddressModeW;

	/// <inheritdoc cref="GpuSamplerParams.MagFilter"/>
	public FilterMode MagFilter => Params.MagFilter;

	/// <inheritdoc cref="GpuSamplerParams.MinFilter"/>
	public FilterMode MinFilter => Params.MinFilter;

	/// <inheritdoc cref="GpuSamplerParams.MipmapFilter"/>
	public MipmapFilterMode MipmapFilter => Params.MipmapFilter;

	/// <inheritdoc cref="GpuSamplerParams.LodMinClamp"/>
	public float LodMinClamp => Params.LodMinClamp;

	/// <inheritdoc cref="GpuSamplerParams.LodMaxClamp"/>
	public float LodMaxClamp => Params.LodMaxClamp;

	/// <inheritdoc cref="GpuSamplerParams.Compare"/>
	public CompareFunction Compare => Params.Compare;

	/// <inheritdoc cref="GpuSamplerParams.MaxAnisotropy"/>
	public ushort MaxAnisotropy => Params.MaxAnisotropy;
}

/// <summary>
/// Owning wrapper around a sampler.
/// </summary>
public sealed class GpuSampler : GpuSamplerHandle, IDisposable {
	private WGPUSampler sampler;

	internal GpuSampler(WGPUSampler sampler, in GpuSamplerParams @params) {
		this.sampler = sampler;
		Params = @params;
	}

	internal override WGPUSampler WgpuSampler {
		get {
			ObjectDisposedException.ThrowIf(sampler.IsNull, this);
			return sampler;
		}
	}
	/// <inheritdoc/>
	public override GpuSamplerParams Params { get; }

	/// <summary>
	/// Creates a non-owning view of this sampler.
	/// </summary>
	public GpuSamplerRef AsRef() => new(this);

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
	/// <inheritdoc/>
	public override GpuSamplerParams Params => source.Params;
}

/// <summary>
/// Parameters used to create a <see cref="GpuSampler"/>.
/// </summary>
/// <param name="AddressModeU">Address mode for the U axis.</param>
/// <param name="AddressModeV">Address mode for the V axis; ignored for 1D textures.</param>
/// <param name="AddressModeW">Address mode for the W axis; ignored for non-3D textures.</param>
/// <param name="MagFilter">
/// Filter when the texture is magnified, i.e. when the level of detail is at most 0; see
/// <see cref="MipmapFilterMode"/>. Must be <see cref="FilterMode.Linear"/> for anisotropic
/// filtering, i.e. <see cref="MaxAnisotropy"/> &gt; 1.
/// </param>
/// <param name="MinFilter">
/// Filter when the texture is minified, i.e. when the level of detail is above 0, applied within
/// each mip level that <paramref name="MipmapFilter"/> selects. Must be
/// <see cref="FilterMode.Linear"/> for anisotropic filtering, i.e. <see cref="MaxAnisotropy"/>
/// &gt; 1.
/// </param>
/// <param name="MipmapFilter">
/// How the sampler selects and blends mip levels. Must be <see cref="MipmapFilterMode.Linear"/>
/// for anisotropic filtering, i.e. <see cref="MaxAnisotropy"/> &gt; 1.
/// </param>
/// <param name="LodMinClamp">
/// Lower bound the level of detail is clamped to, relative to the view's base mip level; must not
/// be negative.
/// </param>
/// <param name="LodMaxClamp">
/// Upper bound the level of detail is clamped to, relative to the view's base mip level; must not
/// be smaller than <paramref name="LodMinClamp"/>. 0 restricts sampling to the base mip level.
/// </param>
/// <param name="Compare">
/// Comparison function, or <see cref="CompareFunction.Undefined"/> for a non-comparison sampler. A
/// comparison sampler has to be bound as <see cref="SamplerBindingType.Comparison"/>; also see
/// <see cref="CompareFunction"/> for what comparison samplers compare.
/// </param>
/// <param name="MaxAnisotropy">
/// Maximum anisotropy; 1 disables anisotropic filtering. WebGPU clamps larger values to what the
/// platform supports, typically 16. Requires all three filters to be linear.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct GpuSamplerParams(
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
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
