// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Which aspects of a texture a view or copy covers.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUTextureAspect), Subset = true)]
public readonly partial struct TextureAspect {
	/// <summary>Raw switch tag for <see cref="TextureAspect"/>.</summary>
	public enum Case {
		/// <summary>
		/// All aspects of the format.
		/// </summary>
		/// <remarks>
		/// Copies of a combined depth/stencil format can't use this; they copy one aspect at a time
		/// with <see cref="DepthOnly"/> or <see cref="StencilOnly"/>.
		/// </remarks>
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
