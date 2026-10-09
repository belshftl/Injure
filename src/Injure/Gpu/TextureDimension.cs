// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The dimensionality of a texture.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUTextureDimension))]
public readonly partial struct TextureDimension {
	/// <summary>Raw switch tag for <see cref="TextureDimension"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="Dimension2d"/>.
		/// </summary>
		Undefined = 0,

		/// <summary>One-dimensional.</summary>
		Dimension1d = 1,

		/// <summary>
		/// Two-dimensional, optionally with array layers.
		/// </summary>
		Dimension2d = 2,

		/// <summary>Three-dimensional.</summary>
		Dimension3d = 3,
	}
}
