// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes a depth/stencil attachment in a render pipeline.
/// </summary>
/// <param name="Format">
/// Format of the depth/stencil attachment; must match the format of the view a render pass uses as
/// that attachment.
/// </param>
/// <param name="DepthWriteEnabled">
/// Whether fragments that pass the depth and stencil tests write their depth.
/// </param>
/// <param name="DepthCompare">
/// Depth test; see <see cref="CompareFunction"/>. <see cref="CompareFunction.Always"/> passes every
/// fragment.
/// </param>
/// <param name="StencilFront">
/// Stencil test and operations for front-facing triangles, and for points/lines. See
/// <see cref="StencilFaceState.Keep"/> if the stencil isn't used.
/// </param>
/// <param name="StencilBack">
/// Stencil test and operations for back-facing triangles. See <see cref="StencilFaceState.Keep"/>
/// if the stencil isn't used.
/// </param>
/// <param name="StencilReadMask">
/// Bits of the stencil value and reference that stencil tests compare.
/// </param>
/// <param name="StencilWriteMask">Bits of the stencil value that stencil operations write.</param>
/// <param name="DepthBias">
/// Constant added to each fragment's depth, in units of the smallest depth difference the format
/// can represent.
/// </param>
/// <param name="DepthBiasSlopeScale">
/// Factor of the triangle's maximum depth slope added to each fragment's depth.
/// </param>
/// <param name="DepthBiasClamp">
/// Limit of the total depth bias. A positive value caps it from above, a negative value caps it
/// from below, and 0 disables the limit.
/// </param>
/// <remarks>
/// <para>
/// Depth bias only applies to triangles; pipelines with point/line topologies must leave
/// <see cref="DepthBias"/>, <see cref="DepthBiasSlopeScale"/>, and <see cref="DepthBiasClamp"/> at
/// 0.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <see cref="Format"/> is
/// <see cref="TextureFormat.Undefined"/>.
/// </para>
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
) : IHasInvalidDefault {
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

	bool IHasInvalidDefault.IsDefault => this == default;
}
