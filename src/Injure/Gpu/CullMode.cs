// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Which triangles to discard based on their facing.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUCullMode))]
public readonly partial struct CullMode {
	/// <summary>Raw switch tag for <see cref="CullMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="None"/>.
		/// </summary>
		Undefined = 0,

		/// <summary>Discards nothing.</summary>
		None = 1,

		/// <summary>
		/// Discards front-facing triangles.
		/// </summary>
		Front = 2,

		/// <summary>
		/// Discards back-facing triangles.
		/// </summary>
		Back = 3,
	}
}
