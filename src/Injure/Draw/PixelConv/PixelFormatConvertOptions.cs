// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Draw.PixelConv;

/// <summary>
/// Lossy conversions to allow; <see cref="PixelFormatConverter"/> refuses each of them unless its
/// flag is set.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedFlags]
public readonly partial struct PixelFormatConversionFlags {
	/// <summary>Raw bits for <see cref="PixelFormatConversionFlags"/>.</summary>
	[Flags]
	public enum Bits {
		/// <summary>No lossy conversions.</summary>
		None = 0,

		/// <summary>
		/// Allows converting a channel to fewer bits than the original.
		/// </summary>
		AllowNarrowing = 1 << 0,

		/// <summary>
		/// Allows converting from a format with alpha to a format without alpha.
		/// </summary>
		AllowDroppingAlpha = 1 << 1,

		/// <summary>
		/// Allows converting to a format that lacks one or more of the source's R/G/B channels.
		/// </summary>
		AllowDroppingColorChannels = 1 << 2,

		/// <summary>
		/// Both <see cref="AllowDroppingAlpha"/> and <see cref="AllowDroppingColorChannels"/>.
		/// </summary>
		AllowDroppingChannels = AllowDroppingAlpha | AllowDroppingColorChannels,
	}
}

/// <summary>
/// Options for a <see cref="PixelFormatConverter"/> conversion.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid, and has an <see cref="Alpha16Unorm"/> of
/// <c>0xffff</c>, <see cref="OverrideAlpha"/> set to <see langword="false"/>, and no flags.
/// </remarks>
public readonly record struct PixelFormatConvertOptions {
	// stored as a complement so that the default value has an alpha of 0xffff
	private readonly ushort alphaComplement;

	/// <summary>
	/// Destination alpha, as 16-bit unorm, used as the fill value if the source has no alpha, and as
	/// the replacement value if the source has alpha and <see cref="OverrideAlpha"/> is set to
	/// <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// Converted to the destination's alpha bits like any other channel. Ignored if the destination
	/// has no alpha.
	/// </remarks>
	public ushort Alpha16Unorm {
		get => (ushort)~alphaComplement;
		init => alphaComplement = (ushort)~value;
	}

	/// <summary>
	/// Whether <see cref="Alpha16Unorm"/> should be used as the destination alpha, even if the source
	/// already has its own alpha.
	/// </summary>
	public bool OverrideAlpha { get; init; }

	/// <summary>
	/// Lossy conversions to allow.
	/// </summary>
	public PixelFormatConversionFlags Flags { get; init; }

	/// <summary>
	/// Creates a <see cref="PixelFormatConvertOptions"/>.
	/// </summary>
	/// <param name="alpha16Unorm">See <see cref="Alpha16Unorm"/>.</param>
	/// <param name="overrideAlpha">See <see cref="OverrideAlpha"/>.</param>
	/// <param name="flags">See <see cref="Flags"/>.</param>
	public PixelFormatConvertOptions(
		ushort alpha16Unorm = 0xffff,
		bool overrideAlpha = false,
		PixelFormatConversionFlags flags = default
	) {
		Alpha16Unorm = alpha16Unorm;
		OverrideAlpha = overrideAlpha;
		Flags = flags;
	}
}
