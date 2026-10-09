// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a texture view interprets the texture's dimensions and layers.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUTextureViewDimension))]
public readonly partial struct TextureViewDimension {
	/// <summary>Raw switch tag for <see cref="TextureViewDimension"/>.</summary>
	public enum Case {
		/// <summary>No value.</summary>
		Undefined = 0,

		/// <summary>A 1D texture.</summary>
		Dimension1d = 1,

		/// <summary>
		/// A single layer of a 2D texture.
		/// </summary>
		Dimension2d = 2,

		/// <summary>
		/// An array of 2D layers.
		/// </summary>
		Dimension2dArray = 3,

		/// <summary>
		/// Six 2D layers forming a cube map.
		/// </summary>
		DimensionCube = 4,

		/// <summary>
		/// An array of cube maps, six layers each.
		/// </summary>
		DimensionCubeArray = 5,

		/// <summary>A 3D texture.</summary>
		Dimension3d = 6,
	}
}
