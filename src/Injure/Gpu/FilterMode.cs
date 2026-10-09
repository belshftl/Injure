// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a sampler filters between texels when magnifying or minifying.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUFilterMode))]
public readonly partial struct FilterMode {
	/// <summary>Raw switch tag for <see cref="FilterMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="Nearest"/>.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// Uses the nearest texel.
		/// </summary>
		Nearest = 1,

		/// <summary>
		/// Interpolates linearly between neighboring texels.
		/// </summary>
		Linear = 2,
	}
}
