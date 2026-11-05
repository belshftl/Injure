// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Gpu;

/// <summary>
/// Samples per texel of a texture, or pixel of a render pipeline.
/// </summary>
/// <remarks>
/// <para>
/// WebGPU only allows either 1 or 4 for sample counts, so the only two members are
/// <see cref="One"/> and <see cref="Four"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="One"/>.
/// </para>
/// </remarks>
[ClosedEnum(CheckZeroName = false)]
public readonly partial struct SampleCount {
	/// <summary>Raw switch tag for <see cref="SampleCount"/>.</summary>
	public enum Case {
		/// <summary>
		/// One sample, i.e. not multisampled.
		/// </summary>
		One,

		/// <summary>
		/// Four samples.
		/// </summary>
		Four,
	}

}

/// <summary>
/// Helpers for <see cref="SampleCount"/>.
/// </summary>
public static class SampleCountExtensions {
	extension(SampleCount sampleCount) {
		/// <summary>
		/// The number of samples, 1 or 4.
		/// </summary>
		public uint Count => sampleCount.RawTag switch {
			SampleCount.Case.One => 1,
			SampleCount.Case.Four => 4,
			_ => throw new UnreachableException(),
		};
	}
}
