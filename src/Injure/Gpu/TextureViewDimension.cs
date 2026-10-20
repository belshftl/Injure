// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a texture view interprets the texture's dimensions and layers.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUTextureViewDimension), Subset = true)]
public readonly partial struct TextureViewDimension {
	/// <summary>Raw switch tag for <see cref="TextureViewDimension"/>.</summary>
	public enum Case {
		/// <summary>A 1D texture; requires <see cref="TextureDimension.Dimension1d"/>.</summary>
		Dimension1d = 1,

		/// <summary>
		/// A single layer of a 2D texture, the view's base array layer; requires
		/// <see cref="TextureDimension.Dimension2d"/>.
		/// </summary>
		Dimension2d = 2,

		/// <summary>
		/// An array of layers of a 2D texture; requires <see cref="TextureDimension.Dimension2d"/>.
		/// </summary>
		Dimension2dArray = 3,

		/// <summary>
		/// Six layers of a 2D texture forming a cube map, as the faces +X, -X, +Y, -Y, +Z, and -Z in
		/// that order; requires <see cref="TextureDimension.Dimension2d"/> and square layers.
		/// </summary>
		/// <remarks>
		/// Cube views can only be sampled; they can't be render attachments or storage textures.
		/// </remarks>
		DimensionCube = 4,

		/// <summary>
		/// An array of cube maps, six layers each, laid out as for <see cref="DimensionCube"/>; the
		/// layer count must be a multiple of 6.
		/// </summary>
		/// <remarks>
		/// Cube views can only be sampled; they can't be render attachments or storage textures.
		/// </remarks>
		DimensionCubeArray = 5,

		/// <summary>A 3D texture; requires <see cref="TextureDimension.Dimension3d"/>.</summary>
		Dimension3d = 6,
	}
}
