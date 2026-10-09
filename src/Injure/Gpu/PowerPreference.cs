// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Which kind of GPU to prefer when several are available.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>, i.e. no preference.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUPowerPreference))]
public readonly partial struct PowerPreference {
	/// <summary>Raw switch tag for <see cref="PowerPreference"/>.</summary>
	public enum Case {
		/// <summary>No preference.</summary>
		Undefined = 0,

		/// <summary>
		/// Prefer a power-efficient GPU, e.g. an integrated one.
		/// </summary>
		LowPower = 1,

		/// <summary>
		/// Prefer a fast GPU, e.g. a discrete one.
		/// </summary>
		HighPerformance = 2,
	}
}
