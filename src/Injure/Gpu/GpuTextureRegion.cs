// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// A region of texels within one mip level and aspect of a texture.
/// </summary>
/// <param name="X">X offset of the region within the mip level, in texels.</param>
/// <param name="Y">Y offset of the region within the mip level, in texels.</param>
/// <param name="Z">
/// Z offset of the region within the mip level in texels for 3D textures, or the first array layer
/// for 2D textures.
/// </param>
/// <param name="Width">Width of the region in texels.</param>
/// <param name="Height">Height of the region in texels.</param>
/// <param name="DepthOrArrayLayers">
/// Depth of the region in texels for 3D textures, or the number of array layers for 2D textures.
/// </param>
/// <param name="MipLevel">
/// Mip level the region is in. Offsets and sizes are in that level's texels, and the region must
/// fit in its size for non-block-compressed formats; for block-compressed formats, it may extend
/// up to the next block boundary if  (see type-level remarks)
/// </param>
/// <param name="Aspect">
/// Aspect to address. Regions of combined depth/stencil formats must be
/// <see cref="TextureAspect.DepthOnly"/> or <see cref="TextureAspect.StencilOnly"/>.
/// </param>
/// <remarks>
/// <para>
/// For block-compressed formats, the offset and size must be multiples of the block size; if a mip
/// level's size is not a multiple of the block size on one or more axes, the region must extend
/// past its size on those axes up to the next block boundary. Mip level 0 is required to be a
/// multiple of the block size, so this only applies to smaller mip levels. For example, level 3
/// (1x1) of an 8x8 BC7 texture needs the region to have a width/height of 4 (BC7 has 4x4 blocks),
/// corresponding to a data size of 16 bytes (one BC7 block is 16 bytes).
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <see cref="Aspect"/> is invalid.
/// </para>
/// </remarks>
public readonly record struct GpuTextureRegion(
	uint X,
	uint Y,
	uint Z,
	uint Width,
	uint Height,
	uint DepthOrArrayLayers,
	uint MipLevel,
	TextureAspect Aspect
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
