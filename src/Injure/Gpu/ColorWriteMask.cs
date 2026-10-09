// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Which channels a color target writes.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedFlags]
[ClosedFlagsMirror(typeof(WGPUColorWriteMask))]
public readonly partial struct ColorWriteMask {
	/// <summary>Raw bits for <see cref="ColorWriteMask"/>.</summary>
	[Flags]
	public enum Bits : ulong {
		/// <summary>Writes no channels.</summary>
		None = 0ul,

		/// <summary>Writes red.</summary>
		Red = 1ul,

		/// <summary>Writes green.</summary>
		Green = 2ul,

		/// <summary>Writes blue.</summary>
		Blue = 4ul,

		/// <summary>Writes alpha.</summary>
		Alpha = 8ul,

		/// <summary>Writes all channels.</summary>
		All = 15ul,
	}
}
