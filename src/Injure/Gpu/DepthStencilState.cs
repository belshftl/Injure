// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes a depth/stencil attachment in a render pipeline.
/// </summary>
/// <param name="Format">Depth/stencil attachment format.</param>
/// <param name="DepthWriteEnabled">Whether the pipeline can modify depth values.</param>
/// <param name="DepthCompare">Depth comparison function.</param>
/// <param name="StencilFront">Stencil operations for front-facing geometry.</param>
/// <param name="StencilBack">Stencil operations for back-facing geometry.</param>
/// <param name="StencilReadMask">Bitmask applied when reading stencil values.</param>
/// <param name="StencilWriteMask">Bitmask applied when writing stencil values.</param>
/// <param name="DepthBias">Constant depth bias.</param>
/// <param name="DepthBiasSlopeScale">Slope-scaled depth bias factor.</param>
/// <param name="DepthBiasClamp">Clamp applied to the final depth bias.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Format"/> is
/// <see cref="TextureFormat.Undefined"/>.
/// </remarks>
public readonly record struct DepthStencilState(
	TextureFormat Format,
	bool DepthWriteEnabled,
	CompareFunction DepthCompare,
	StencilFaceState StencilFront,
	StencilFaceState StencilBack,
	uint StencilReadMask = uint.MaxValue,
	uint StencilWriteMask = uint.MaxValue,
	int DepthBias = 0,
	float DepthBiasSlopeScale = 0f,
	float DepthBiasClamp = 0f
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUDepthStencilState"/>.
	/// </summary>
	internal WGPUDepthStencilState ToWebgpuType() => new() {
		format = Format.ToWebgpuType(),
		depthWriteEnabled = DepthWriteEnabled ? WGPUOptionalBool.True : WGPUOptionalBool.False,
		depthCompare = DepthCompare.ToWebgpuType(),
		stencilFront = StencilFront.ToWebgpuType(),
		stencilBack = StencilBack.ToWebgpuType(),
		stencilReadMask = StencilReadMask,
		stencilWriteMask = StencilWriteMask,
		depthBias = DepthBias,
		depthBiasSlopeScale = DepthBiasSlopeScale,
		depthBiasClamp = DepthBiasClamp,
	};
}
