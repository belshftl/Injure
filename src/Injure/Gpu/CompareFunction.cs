// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// A comparison between a new value and an existing one, used by depth/stencil tests and comparison
/// samplers.
/// </summary>
/// <remarks>
/// <para>
/// What "new" and "existing" refer to depends on the use:
/// <list type="bullet">
/// <item><description>
/// Depth tests compare the fragment's depth (new) against the value in the depth attachment
/// (existing).
/// </description></item>
/// <item><description>
/// Stencil tests compare the stencil reference (new) against the value in the stencil attachment
/// (existing), both masked by <see cref="DepthStencilState.StencilReadMask"/>.
/// </description></item>
/// <item><description>
/// Comparison samplers compare the reference passed to <c>textureSampleCompare</c> and similar
/// (new) against the texel (existing). Each comparison gives 1.0 if it passes and 0.0 otherwise, and
/// filtering blends those results rather than the texels.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </para>
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUCompareFunction))]
public readonly partial struct CompareFunction {
	/// <summary>Raw switch tag for <see cref="CompareFunction"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value. For samplers, this means the sampler isn't a comparison sampler.
		/// </summary>
		Undefined = 0,

		/// <summary>Never passes.</summary>
		Never = 1,

		/// <summary>
		/// Passes if new &lt; existing.
		/// </summary>
		Less = 2,

		/// <summary>
		/// Passes if new = existing.
		/// </summary>
		Equal = 3,

		/// <summary>
		/// Passes if new &lt;= existing.
		/// </summary>
		LessEqual = 4,

		/// <summary>
		/// Passes if new &gt; existing.
		/// </summary>
		Greater = 5,

		/// <summary>
		/// Passes if new != existing.
		/// </summary>
		NotEqual = 6,

		/// <summary>
		/// Passes if new &gt;= existing.
		/// </summary>
		GreaterEqual = 7,

		/// <summary>Always passes.</summary>
		Always = 8,
	}
}
