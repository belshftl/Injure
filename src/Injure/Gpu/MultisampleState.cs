// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes how a render pipeline interacts with multisampled attachments.
/// </summary>
/// <param name="Count">
/// Samples per pixel, 1 or 4; must match the sample count of the attachments of the render passes
/// the pipeline is used in.
/// </param>
/// <param name="Mask">Samples the pipeline writes; bit <c>i</c> enables sample <c>i</c>.</param>
/// <param name="AlphaToCoverageEnabled">
/// Whether the alpha of the shader output at <c>@location(0)</c> also determines which samples a
/// fragment covers; requires <paramref name="Count"/> to be larger than 1.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its sample count is 0.
/// <c>new MultisampleState()</c> is single-sampled with all samples enabled and alpha-to-coverage
/// disabled.
/// </remarks>
public readonly record struct MultisampleState(
	uint Count = 1,
	uint Mask = uint.MaxValue,
	bool AlphaToCoverageEnabled = false
) : IHasInvalidDefault {
	/// <summary>
	/// Creates a single-sampled state with all samples enabled and alpha-to-coverage disabled.
	/// </summary>
	public MultisampleState() : this(1) {}

	internal WGPUMultisampleState ToWebgpuType() => new() {
		count = Count,
		mask = Mask,
		alphaToCoverageEnabled = AlphaToCoverageEnabled ? WGPUBool.True : WGPUBool.False,
	};

	bool IHasInvalidDefault.IsDefault => this == default;
}
