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
		/// Floating-point values that may be filtered (<c>texture_2d&lt;f32&gt;</c> and similar), from
		/// unorm, snorm, and float formats other than 32-bit float ones.
		/// </summary>
		/// <remarks>
		/// 32-bit float formats can use this with <see cref="GpuFeatures.Float32Filterable"/>.
		/// </remarks>
		Float = 2,

		/// <summary>
		/// Floating-point values that may only be read with a <see cref="SamplerBindingType.NonFiltering"/>
		/// sampler or <c>textureLoad()</c>. Any format that allows <see cref="Float"/> also allows
		/// this, and 32-bit float formats require it unless <see cref="GpuFeatures.Float32Filterable"/>
		/// is enabled.
		/// </summary>
		UnfilterableFloat = 3,

		/// <summary>
		/// Depth values (<c>texture_depth_2d</c> and similar), from a depth format or the depth aspect
		/// of a combined depth/stencil format.
		/// </summary>
		Depth = 4,

		/// <summary>
		/// Signed integers (<c>texture_2d&lt;i32&gt;</c> and similar), from sint formats; only
		/// readable with <c>textureLoad()</c>.
		/// </summary>
		Sint = 5,

		/// <summary>
		/// Unsigned integers (<c>texture_2d&lt;u32&gt;</c> and similar), from uint formats and the
		/// stencil aspect; only readable with <c>textureLoad()</c>.
		/// </summary>
		Uint = 6,
	}
}
