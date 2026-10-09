// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The element type of an index buffer.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUIndexFormat))]
public readonly partial struct IndexFormat {
	/// <summary>Raw switch tag for <see cref="IndexFormat"/>.</summary>
	public enum Case {
		/// <summary>
		/// No format. Required as <see cref="PrimitiveState.StripIndexFormat"/> for non-strip topologies.
		/// </summary>
		Undefined = 0,

		/// <summary>16-bit unsigned indices.</summary>
		Uint16 = 1,

		/// <summary>32-bit unsigned indices.</summary>
		Uint32 = 2,
	}
}
