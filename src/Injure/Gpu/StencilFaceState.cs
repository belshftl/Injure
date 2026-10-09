// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes stencil operations for one face direction.
/// </summary>
/// <param name="Compare">Stencil comparison function.</param>
/// <param name="FailOp">Operation applied when the stencil test fails.</param>
/// <param name="DepthFailOp">
/// Operation applied when the stencil test passes but the depth test fails.
/// </param>
/// <param name="PassOp">Operation applied when both stencil and depth tests pass.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a test that always passes and keeps the
/// stencil value, since WebGPU substitutes <see cref="CompareFunction.Always"/> and
/// <see cref="StencilOperation.Keep"/> for the <c>Undefined</c> members.
/// </remarks>
public readonly record struct StencilFaceState(
	CompareFunction Compare,
	StencilOperation FailOp,
	StencilOperation DepthFailOp,
	StencilOperation PassOp
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUStencilFaceState"/>.
	/// </summary>
	internal WGPUStencilFaceState ToWebgpuType() => new() {
		compare = Compare.ToWebgpuType(),
		failOp = FailOp.ToWebgpuType(),
		depthFailOp = DepthFailOp.ToWebgpuType(),
		passOp = PassOp.ToWebgpuType(),
	};
}
