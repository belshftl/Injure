// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a sampler filters between mip levels.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUMipmapFilterMode))]
public readonly partial struct MipmapFilterMode {
	/// <summary>Raw switch tag for <see cref="MipmapFilterMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="Nearest"/>.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// Uses the nearest mip level.
		/// </summary>
		Nearest = 1,

		/// <summary>
		/// Interpolates linearly between the two nearest mip levels.
		/// </summary>
		Linear = 2,
	}
}
