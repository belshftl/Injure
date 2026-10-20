// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Mip-level selection/blending. Filtering within each mip level uses the sampler's
/// <see cref="GpuSamplerParams.MinFilter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>lambda</c> hereafter refers to the level of detail, derived from screen-space derivatives
/// (<c>textureSample</c>, <c>textureSampleBias</c>) or given explicitly
/// (<c>textureSampleLevel</c>, <c>textureSampleGrad</c>), plus bias, clamped to
/// [<see cref="GpuSamplerParams.LodMinClamp"/>, <see cref="GpuSamplerParams.LodMaxClamp"/>].
/// Mip-level indices are relative to the texture view's base mip level.
/// </para>
/// <para>
/// For <c>lambda &lt;= 0</c>, the sampler reads mip level 0 with
/// <see cref="GpuSamplerParams.MagFilter"/>, and this mode has no effect. This mode also has no
/// effect on single-mip-level views.
/// </para>
/// <para>
/// There's no way to disable mipmapping with this alone; see
/// <see cref="GpuSamplerParams.LodMaxClamp"/> to restrict sampling to mip level 0.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUMipmapFilterMode), Subset = true)]
public readonly partial struct MipmapFilterMode {
	/// <summary>Raw switch tag for <see cref="MipmapFilterMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Samples the single mip level nearest to <c>lambda</c> (see type-level docs). The mip level
		/// that gets chosen if <c>lambda</c> lies exactly halfway between two mip levels is
		/// implementation-defined.
		/// </summary>
		Nearest = 1,

		/// <summary>
		/// Samples the two mip levels adjacent to <c>lambda</c> (see type-level docs), those being
		/// <c>floor(lambda)</c> and the next mip level, clamped to the view's last mip level, and blends
		/// them by <c>fract(lambda)</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Blend weights are quantized (usually to 1/256), not at full float precision. Interpolation
		/// results are therefore not full-float accurate.
		/// </para>
		/// <para>
		/// Requires a texture with a filterable sample type and a sampler bound with
		/// <see cref="SamplerBindingType.Filtering"/>.
		/// </para>
		/// <para>
		/// Required for anisotropic filtering, i.e. <see cref="GpuSamplerParams.MaxAnisotropy"/> &gt; 1.
		/// </para>
		/// </remarks>
		Linear = 2,
	}
}
