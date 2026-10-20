// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// What a stencil test does to the stencil value.
/// </summary>
/// <remarks>
/// <para>
/// Stencil values are 8-bit, so the maximum is 255. Only the bits set in
/// <see cref="DepthStencilState.StencilWriteMask"/> are written.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUStencilOperation), Subset = true)]
public readonly partial struct StencilOperation {
	/// <summary>Raw switch tag for <see cref="StencilOperation"/>.</summary>
	public enum Case {
		/// <summary>Keeps the value.</summary>
		Keep = 1,

		/// <summary>Sets the value to 0.</summary>
		Zero = 2,

		/// <summary>
		/// Sets the value to the reference set with <see cref="RenderPass.SetStencilReference(uint)"/>.
		/// </summary>
		Replace = 3,

		/// <summary>
		/// Inverts the bits of the value.
		/// </summary>
		Invert = 4,

		/// <summary>
		/// Increments the value, clamping at the maximum.
		/// </summary>
		IncrementClamp = 5,

		/// <summary>
		/// Decrements the value, clamping at 0.
		/// </summary>
		DecrementClamp = 6,

		/// <summary>
		/// Increments the value, wrapping to 0.
		/// </summary>
		IncrementWrap = 7,

		/// <summary>
		/// Decrements the value, wrapping to the maximum.
		/// </summary>
		DecrementWrap = 8,
	}
}
