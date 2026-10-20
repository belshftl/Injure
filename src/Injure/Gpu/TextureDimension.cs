// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The dimensionality of a texture.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUTextureDimension), Subset = true)]
public readonly partial struct TextureDimension {
	/// <summary>Raw switch tag for <see cref="TextureDimension"/>.</summary>
	public enum Case {
		/// <summary>
		/// One-dimensional. The height and <see cref="GpuTextureParams.DepthOrArrayLayers"/> must be 1,
		/// and the texture can have only one mip level and a sample count of one.
		/// </summary>
		Dimension1d = 1,

		/// <summary>
		/// Two-dimensional, with <see cref="GpuTextureParams.DepthOrArrayLayers"/> as the number of
		/// array layers. Mip levels halve the width and height but keep every layer.
		/// </summary>
		Dimension2d = 2,

		/// <summary>
		/// Three-dimensional, with <see cref="GpuTextureParams.DepthOrArrayLayers"/> as the depth. Mip
		/// levels halve all three dimensions.
		/// </summary>
		Dimension3d = 3,
	}
}
