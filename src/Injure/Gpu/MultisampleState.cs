// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes how a render pipeline interacts with multisampled attachments.
/// </summary>
/// <param name="Count">Samples per pixel.</param>
/// <param name="Mask">Bitmask determining which samples are written to.</param>
/// <param name="AlphaToCoverageEnabled">Whether alpha-to-coverage is enabled.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its sample count is 0.
/// <c>new MultisampleState()</c> is single-sampled with all samples enabled.
/// </remarks>
public readonly record struct MultisampleState(
	uint Count = 1,
	uint Mask = uint.MaxValue,
	bool AlphaToCoverageEnabled = false
) {
	/// <summary>
	/// Creates a single-sampled state with all samples enabled and alpha-to-coverage disabled.
	/// </summary>
	public MultisampleState() : this(1) {}

	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUMultisampleState"/>.
	/// </summary>
	internal WGPUMultisampleState ToWebgpuType() => new() {
		count = Count,
		mask = Mask,
		alphaToCoverageEnabled = AlphaToCoverageEnabled ? WGPUBool.True : WGPUBool.False,
	};
}
