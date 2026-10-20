// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a <see cref="BlendComponent"/> combines the weighted source and destination values.
/// </summary>
/// <remarks>
/// <para>
/// Results are clamped to the attachment format's range, e.g. [0.0, 1.0] for unorm formats, so
/// <see cref="Subtract"/>/<see cref="ReverseSubtract"/> can't go negative there.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUBlendOperation), Subset = true)]
public readonly partial struct BlendOperation {
	/// <summary>Raw switch tag for <see cref="BlendOperation"/>.</summary>
	public enum Case {
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
		/// The minimum of source and destination, without factors; both factors must be
		/// <see cref="BlendFactor.One"/>.
		/// </summary>
		Min = 4,

		/// <summary>
		/// The maximum of source and destination, without factors; both factors must be
		/// <see cref="BlendFactor.One"/>.
		/// </summary>
		Max = 5,
	}
}
