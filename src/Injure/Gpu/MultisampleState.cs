// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes how a render pipeline interacts with multisampled attachments.
/// </summary>
/// <param name="Count">
/// Samples per pixel; must match the sample count of the attachments of the render passes the
/// pipeline is used in.
/// </param>
/// <param name="Mask">Samples the pipeline writes; bit <c>i</c> enables sample <c>i</c>.</param>
/// <param name="AlphaToCoverageEnabled">
/// Whether the alpha of the shader output at <c>@location(0)</c> also determines which samples a
/// fragment covers; requires <paramref name="Count"/> to be <see cref="SampleCount.Four"/>.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is valid, but since its <paramref name="Mask"/> is 0, it
/// writes no samples. See <see cref="SingleSampled"/> for a standard single-sampled pipeline.
/// </remarks>
public readonly record struct MultisampleState(
	SampleCount Count,
	uint Mask,
	bool AlphaToCoverageEnabled
) {
	/// <summary>
	/// A single-sampled state with all samples enabled and alpha-to-coverage disabled.
	/// </summary>
	public static readonly MultisampleState SingleSampled = new(SampleCount.One, uint.MaxValue, false);

	internal WGPUMultisampleState ToWebgpuType() => new() {
		count = Count.Count,
		mask = Mask,
		alphaToCoverageEnabled = AlphaToCoverageEnabled ? WGPUBool.True : WGPUBool.False,
	};
}
