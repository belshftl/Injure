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
/// The <see langword="default"/> value is invalid. For a face that doesn't use the stencil, see
/// <see cref="Keep"/>.
/// </remarks>
public readonly record struct StencilFaceState(
	CompareFunction Compare,
	StencilOperation FailOp,
	StencilOperation DepthFailOp,
	StencilOperation PassOp
) : IHasInvalidDefault {
	/// <summary>
	/// <see cref="CompareFunction.Always"/> + <see cref="StencilOperation.Keep"/> on all three ops,
	/// i.e. a test that always passes and keeps the stencil value, useful for a face that doesn't use
	/// the stencil.
	/// </summary>
	public static readonly StencilFaceState Keep = new(
		CompareFunction.Always,
		StencilOperation.Keep,
		StencilOperation.Keep,
		StencilOperation.Keep
	);

	internal WGPUStencilFaceState ToWebgpuType() => new() {
		compare = Compare.ToWebgpuType(),
		failOp = FailOp.ToWebgpuType(),
		depthFailOp = DepthFailOp.ToWebgpuType(),
		passOp = PassOp.ToWebgpuType(),
	};

	bool IHasInvalidDefault.IsDefault => this == default;
}
