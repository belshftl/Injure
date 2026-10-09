// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The kind of sampler a <see cref="GpuSamplerBindingLayout"/> binds.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUSamplerBindingType), Subset = true)]
public readonly partial struct SamplerBindingType {
	/// <summary>Raw switch tag for <see cref="SamplerBindingType"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="Filtering"/>.
		/// </summary>
		Undefined = 1,

		/// <summary>
		/// A sampler that may filter (e.g. <see cref="FilterMode.Linear"/>).
		/// </summary>
		Filtering = 2,

		/// <summary>
		/// A sampler that only uses nearest filtering; needed for unfilterable textures.
		/// </summary>
		NonFiltering = 3,

		/// <summary>
		/// A comparison sampler, i.e. one with a <see cref="CompareFunction"/>.
		/// </summary>
		Comparison = 4,
	}
}
