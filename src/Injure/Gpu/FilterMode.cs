// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Texel filter applied within a single mip level when magnifying or minifying.
/// </summary>
/// <remarks>
/// <para>
/// Texel <c>k</c> covers coordinates <c>[k, k + 1.0)</c> in texel space, with its center at
/// <c>k + 0.5</c>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUFilterMode), Subset = true)]
public readonly partial struct FilterMode {
	/// <summary>Raw switch tag for <see cref="FilterMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Returns the texel at <c>(floor(u), floor(v), floor(w))</c>, truncated to the texture's
		/// dimensionality.
		/// </summary>
		/// <remarks>
		/// The result is implementation-defined for coordinates at or very near a texel boundary;
		/// hardware quantizes coordinates to a sub-texel grid with implementation-defined rounding. If
		/// you want exact texel addressing, sample at texel centers, or use <c>textureLoad()</c>.
		/// </remarks>
		Nearest = 1,

		/// <summary>
		/// Blends the 2 (1D), 4 (2D), or 8 (3D) texels nearest to the coordinate, weighted by distance
		/// to said texels' centers. Coordinates exactly at texel centers return exactly that texel.
		/// Neighbors outside the texture are resolved by the address mode.
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
