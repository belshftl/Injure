// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Which winding order makes a triangle front-facing.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUFrontFace))]
public readonly partial struct FrontFace {
	/// <summary>Raw switch tag for <see cref="FrontFace"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="Ccw"/>.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// Counter-clockwise vertices, as seen on screen, are front-facing.
		/// </summary>
		Ccw = 1,

		/// <summary>
		/// Clockwise vertices, as seen on screen, are front-facing.
		/// </summary>
		Cw = 2,
	}
}
