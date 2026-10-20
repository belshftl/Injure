// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a sampler handles texture coordinates outside [0.0, 1.0].
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUAddressMode), Subset = true)]
public readonly partial struct AddressMode {
	/// <summary>Raw switch tag for <see cref="AddressMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Texture coordinates are clamped to [0.0, 1.0].
		/// </summary>
		ClampToEdge = 1,

		/// <summary>
		/// Texture coordinates wrap to the other side of the texture.
		/// </summary>
		Repeat = 2,

		/// <summary>
		/// Texture coordinates wrap to the other side of the texture, and the texture is additionally
		/// flipped if the coordinate, rounded to a whole number towards negative infinity, is odd.
		/// </summary>
		MirrorRepeat = 3,
	}
}
