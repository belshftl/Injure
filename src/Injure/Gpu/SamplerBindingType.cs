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
		/// A non-comparison sampler with any filter modes.
		/// </summary>
		Filtering = 2,

		/// <summary>
		/// A non-comparison sampler whose <see cref="GpuSamplerParams.MagFilter"/>,
		/// <see cref="GpuSamplerParams.MinFilter"/>, and <see cref="GpuSamplerParams.MipmapFilter"/>
		/// are all nearest; required for sampling <see cref="TextureSampleType.UnfilterableFloat"/>
		/// textures.
		/// </summary>
		NonFiltering = 3,

		/// <summary>
		/// A comparison sampler, i.e. one whose <see cref="GpuSamplerParams.Compare"/> isn't
		/// <see cref="CompareFunction.Undefined"/> (<c>sampler_comparison</c> in WGSL).
		/// </summary>
		Comparison = 4,
	}
}
