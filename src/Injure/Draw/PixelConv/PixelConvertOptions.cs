// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Draw.PixelConv;

/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedFlags]
public readonly partial struct ConversionFlags {
	[Flags]
	public enum Bits {
		None = 0,
		AllowNarrowing = 1 << 0,
		AllowDroppingAlpha = 1 << 1,
		AllowDroppingColorChannels = 1 << 2,
		AllowDroppingChannels = AllowDroppingAlpha | AllowDroppingColorChannels,
	}
}

/// <remarks>
/// The <see langword="default"/> value is valid and stands for <c>new PixelConvertOptions()</c>,
/// which isn't the <see langword="default"/> value itself.
/// </remarks>
public readonly record struct PixelConvertOptions(
	ushort Alpha16Unorm = 0xffff,
	bool OverrideAlpha = false,
	ConversionFlags Flags = default
) {
	public PixelConvertOptions() : this(0xffff) {}
	private readonly int dummy = 1;
	internal bool IsDefault => dummy == 0;
}
