// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes how the color/alpha of a fragment is blended.
/// </summary>
/// <param name="Operation">Blend operation to apply.</param>
/// <param name="SrcFactor">Factor applied to the source value.</param>
/// <param name="DstFactor">Factor applied to the destination value.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="BlendOperation.Add"/> with
/// factors <see cref="BlendFactor.One"/> and <see cref="BlendFactor.Zero"/>, i.e. the source
/// replaces the destination, since WebGPU substitutes those for the <c>Undefined</c> members.
/// </remarks>
public readonly record struct BlendComponent(
	BlendOperation Operation,
	BlendFactor SrcFactor,
	BlendFactor DstFactor
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUBlendComponent"/>.
	/// </summary>
	internal WGPUBlendComponent ToWebgpuType() => new() {
		operation = Operation.ToWebgpuType(),
		srcFactor = SrcFactor.ToWebgpuType(),
		dstFactor = DstFactor.ToWebgpuType(),
	};
}
