// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes color and alpha blending state for a color target.
/// </summary>
/// <param name="Color">Blend behavior for the r/g/b components.</param>
/// <param name="Alpha">Blend behavior for the alpha component.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid. For a target where the source replaces the
/// destination, keep <see cref="ColorTargetState.Blend"/> set to <see langword="null"/>.
/// </remarks>
public readonly record struct BlendState(
	BlendComponent Color,
	BlendComponent Alpha
) : IHasInvalidDefault {
	internal WGPUBlendState ToWebgpuType() => new() {
		color = Color.ToWebgpuType(),
		alpha = Alpha.ToWebgpuType(),
	};

	bool IHasInvalidDefault.IsDefault => this == default;
}
