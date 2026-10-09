// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes color and alpha blending state for a color target.
/// </summary>
/// <param name="Color">Blend behavior for the color components.</param>
/// <param name="Alpha">Blend behavior for the alpha component.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and makes the source replace the destination for
/// both color and alpha; see <see cref="BlendComponent"/>.
/// </remarks>
public readonly record struct BlendState(
	BlendComponent Color,
	BlendComponent Alpha
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUBlendState"/>.
	/// </summary>
	internal WGPUBlendState ToWebgpuType() => new() {
		color = Color.ToWebgpuType(),
		alpha = Alpha.ToWebgpuType(),
	};
}
