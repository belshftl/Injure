// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a <see cref="BlendComponent"/> combines the weighted source and destination values.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUBlendOperation))]
public readonly partial struct BlendOperation {
	/// <summary>Raw switch tag for <see cref="BlendOperation"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="Add"/>.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// source &#215; source factor + destination &#215; destination factor.
		/// </summary>
		Add = 1,

		/// <summary>
		/// source &#215; source factor - destination &#215; destination factor.
		/// </summary>
		Subtract = 2,

		/// <summary>
		/// destination &#215; destination factor - source &#215; source factor.
		/// </summary>
		ReverseSubtract = 3,

		/// <summary>
		/// The minimum of source and destination; both factors must be <see cref="BlendFactor.One"/>.
		/// </summary>
		Min = 4,

		/// <summary>
		/// The maximum of source and destination; both factors must be <see cref="BlendFactor.One"/>.
		/// </summary>
		Max = 5,
	}
}
