// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a sampler handles texture coordinates outside [0, 1].
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUAddressMode))]
public readonly partial struct AddressMode {
	/// <summary>Raw switch tag for <see cref="AddressMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="ClampToEdge"/>.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// Clamps coordinates to the edge texels.
		/// </summary>
		ClampToEdge = 1,

		/// <summary>
		/// Repeats the texture.
		/// </summary>
		Repeat = 2,

		/// <summary>
		/// Repeats the texture, mirroring every other repetition.
		/// </summary>
		MirrorRepeat = 3,
	}
}
