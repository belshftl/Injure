// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Which aspects of a texture a view or copy covers.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUTextureAspect))]
public readonly partial struct TextureAspect {
	/// <summary>Raw switch tag for <see cref="TextureAspect"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value. <see cref="GpuTextureHandle.CreateView(in GpuTextureViewCreateParams)"/> rejects it.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// All aspects of the format.
		/// </summary>
		All = 1,

		/// <summary>
		/// Only the stencil aspect of a depth/stencil format.
		/// </summary>
		StencilOnly = 2,

		/// <summary>
		/// Only the depth aspect of a depth/stencil format.
		/// </summary>
		DepthOnly = 3,
	}
}
