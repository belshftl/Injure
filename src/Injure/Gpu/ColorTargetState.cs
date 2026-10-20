// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes a color attachment in a render pipeline.
/// </summary>
/// <param name="Format">
/// Format of the color attachment; must match the format of the view a render pass uses as that
/// attachment.
/// </param>
/// <param name="Blend">
/// Blend state, or <see langword="null"/> for the source to replace the destination. Blending
/// requires a blendable format; 32-bit float formats are only blendable with
/// <see cref="GpuFeatures.Float32Blendable"/>.
/// </param>
/// <param name="WriteMask">
/// Channels the pipeline writes; the attachment keeps its contents in the others.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Format"/> is
/// <see cref="TextureFormat.Undefined"/>.
/// </remarks>
public readonly record struct ColorTargetState(
	TextureFormat Format,
	BlendState? Blend,
	ColorWriteMask WriteMask
) : IHasInvalidDefault {
	/// <remarks>
	/// <see cref="WGPUColorTargetState.blend"/> is a pointer, so this requires scratch storage in the
	/// form of <paramref name="blendStorage"/>, which must be writable for one
	/// <see cref="WGPUBlendState"/> and kept alive at least as long as the returned value.
	/// </remarks>
	internal unsafe WGPUColorTargetState ToWebgpuType(WGPUBlendState* blendStorage) {
		WGPUColorTargetState ret = new() {
			format = Format.ToWebgpuType(),
			writeMask = WriteMask.ToWebgpuType(),
		};
		if (Blend is BlendState b) {
			*blendStorage = b.ToWebgpuType();
			ret.blend = blendStorage;
		}
		return ret;
	}

	bool IHasInvalidDefault.IsDefault => this == default;
}
