// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes how the color/alpha of a fragment is blended: the result is
/// <see cref="Operation"/> applied to the source times <see cref="SrcFactor"/> and the destination
/// times <see cref="DstFactor"/>.
/// </summary>
/// <param name="Operation">How the weighted source and destination combine.</param>
/// <param name="SrcFactor">
/// Factor that the source, i.e. the fragment shader output, is multiplied by.
/// </param>
/// <param name="DstFactor">
/// Factor that the destination, i.e. what the attachment already contains, is multiplied by.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct BlendComponent(
	BlendOperation Operation,
	BlendFactor SrcFactor,
	BlendFactor DstFactor
) : IHasInvalidDefault {
	internal WGPUBlendComponent ToWebgpuType() => new() {
		operation = Operation.ToWebgpuType(),
		srcFactor = SrcFactor.ToWebgpuType(),
		dstFactor = DstFactor.ToWebgpuType(),
	};

	bool IHasInvalidDefault.IsDefault => this == default;
}
