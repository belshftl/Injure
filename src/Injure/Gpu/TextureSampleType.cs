// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The kind of values a shader reads from a texture bound by a
/// <see cref="GpuTextureBindingLayout"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUTextureSampleType), Subset = true)]
public readonly partial struct TextureSampleType {
	/// <summary>Raw switch tag for <see cref="TextureSampleType"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="Float"/>.
		/// </summary>
		Undefined = 1,

		/// <summary>
		/// Floating-point values that may be filtered.
		/// </summary>
		Float = 2,

		/// <summary>
		/// Floating-point values that may only be read with a non-filtering sampler, e.g. from 32-bit
		/// float formats.
		/// </summary>
		UnfilterableFloat = 3,

		/// <summary>Depth values.</summary>
		Depth = 4,

		/// <summary>Signed integers.</summary>
		Sint = 5,

		/// <summary>Unsigned integers.</summary>
		Uint = 6,
	}
}
