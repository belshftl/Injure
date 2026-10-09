// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// A factor that a <see cref="BlendComponent"/> multiplies the source or destination value by.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUBlendFactor))]
public readonly partial struct BlendFactor {
	/// <summary>Raw switch tag for <see cref="BlendFactor"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="One"/> for source factors and <see cref="Zero"/> for
		/// destination factors.
		/// </summary>
		Undefined = 0,

		/// <summary>0.</summary>
		Zero = 1,

		/// <summary>1.</summary>
		One = 2,

		/// <summary>
		/// The source value, i.e. the fragment shader output.
		/// </summary>
		Src = 3,

		/// <summary>
		/// 1 minus the source value.
		/// </summary>
		OneMinusSrc = 4,

		/// <summary>The source alpha.</summary>
		SrcAlpha = 5,

		/// <summary>1 minus the source alpha.</summary>
		OneMinusSrcAlpha = 6,

		/// <summary>
		/// The destination value, i.e. what the attachment already contains.
		/// </summary>
		Dst = 7,

		/// <summary>
		/// 1 minus the destination value.
		/// </summary>
		OneMinusDst = 8,

		/// <summary>The destination alpha.</summary>
		DstAlpha = 9,

		/// <summary>1 minus the destination alpha.</summary>
		OneMinusDstAlpha = 10,

		/// <summary>
		/// The minimum of the source alpha and 1 minus the destination alpha (1 for the alpha channel).
		/// </summary>
		SrcAlphaSaturated = 11,

		/// <summary>
		/// The blend constant set with <see cref="RenderPass.SetBlendConstant(Vector4)"/>.
		/// </summary>
		Constant = 12,

		/// <summary>
		/// 1 minus the blend constant.
		/// </summary>
		OneMinusConstant = 13,

		/// <summary>
		/// The second fragment shader output (dual-source blending). Requires
		/// <see cref="GpuFeatures.DualSourceBlending"/>.
		/// </summary>
		Src1 = 14,

		/// <summary>
		/// 1 minus the second fragment shader output (dual-source blending). Requires
		/// <see cref="GpuFeatures.DualSourceBlending"/>.
		/// </summary>
		OneMinusSrc1 = 15,

		/// <summary>
		/// The alpha of the second fragment shader output (dual-source blending). Requires
		/// <see cref="GpuFeatures.DualSourceBlending"/>.
		/// </summary>
		Src1Alpha = 16,

		/// <summary>
		/// 1 minus the alpha of the second fragment shader output (dual-source blending). Requires
		/// <see cref="GpuFeatures.DualSourceBlending"/>.
		/// </summary>
		OneMinusSrc1Alpha = 17,
	}
}
