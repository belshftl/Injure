// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Draw.PixelConv;

/// <summary>
/// How pixels are stored in memory.
/// </summary>
/// <remarks>
/// <para>
/// Pixels are tightly packed with no padding; for example, four <see cref="Rgb24Unorm"/> pixels
/// occupy 12 bytes, not e.g. 16.
/// </para>
/// <para>
/// Each format's name specifies, in order:
/// <list type="number">
/// <item><description>
/// The channels, for example <c>Rgb</c> for red+green+blue or <c>La</c> for luminance+alpha, in
/// the same order the bytes are stored in memory, and MSB to LSB within a byte.
/// </description></item>
/// <item><description>
/// If the format is not packed (see below), the amount of bits in a whole pixel, for example
/// <c>24</c>; if the format is packed, the amount of bits in each channel, in the same order as
/// the earlier-specified channels, for example <c>565</c> or <c>4444</c>.
/// </description></item>
/// <item><description>
/// Numeric kind of the channels, for example <c>Unorm</c>.
/// </description></item>
/// <item><description>
/// If the format is packed, <c>PackN</c> where <c>N</c> is the amount of bits in a whole pixel,
/// for example <c>Pack16</c>. This is what defines whether the format is packed or not.
/// </description></item>
/// <item><description>
/// If the format is not packed and has more than one byte per channel, the byte order <b>within
/// each channel</b>; if the format is packed, the byte order <b>within one pixel</b>. Byte order
/// is listed as either <c>Le</c> for little-endian or <c>Be</c> for big-endian.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Converting <b>from</b> a gray format (<see cref="L8Unorm"/> and <see cref="La16Unorm"/>) writes
/// the gray value into each color channel of the destination. Converting <b>to</b> a gray format
/// is disallowed unless the source format has exactly one channel (e.g. <see cref="R8Unorm"/>),
/// which converts to gray as-is. The rationale is that there is no unopinionated way to convert an
/// arbitrary color image to grayscale; the sanest default would be something like Rec. 709 luma
/// weights, which is still not one-size-fits-all and a bit out of scope for
/// <see cref="PixelFormatConverter"/>.
/// </para>
/// <para>
/// Only storage is described; premultiplied alpha, transfer function, and color space aren't
/// encoded.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct PixelFormat {
	/// <summary>Raw switch tag for <see cref="PixelFormat"/>.</summary>
	public enum Case {
		/// <summary>
		/// RGBA as four 8-bit unsigned normalized channels, in that order in memory.
		/// </summary>
		Rgba32Unorm = 1,

		/// <summary>
		/// BGRA as four 8-bit unsigned normalized channels, in that order in memory.
		/// </summary>
		Bgra32Unorm,

		/// <summary>
		/// ARGB as four 8-bit unsigned normalized channels, in that order in memory.
		/// </summary>
		Argb32Unorm,

		/// <summary>
		/// ABGR as four 8-bit unsigned normalized channels, in that order in memory.
		/// </summary>
		Abgr32Unorm,

		/// <summary>
		/// RGBA as four 16-bit unsigned normalized channels, in that order in memory, with the
		/// bytes within each channel in little-endian order.
		/// </summary>
		Rgba64UnormLe,

		/// <summary>
		/// RGBA as four 16-bit unsigned normalized channels, in that order in memory, with the
		/// bytes within each channel in big-endian order.
		/// </summary>
		Rgba64UnormBe,

		/// <summary>
		/// BGRA as four 16-bit unsigned normalized channels, in that order in memory, with the
		/// bytes within each channel in little-endian order.
		/// </summary>
		Bgra64UnormLe,

		/// <summary>
		/// BGRA as four 16-bit unsigned normalized channels, in that order in memory, with the
		/// bytes within each channel in big-endian order.
		/// </summary>
		Bgra64UnormBe,

		/// <summary>
		/// ARGB as four 16-bit unsigned normalized channels, in that order in memory, with the
		/// bytes within each channel in little-endian order.
		/// </summary>
		Argb64UnormLe,

		/// <summary>
		/// ARGB as four 16-bit unsigned normalized channels, in that order in memory, with the
		/// bytes within each channel in big-endian order.
		/// </summary>
		Argb64UnormBe,

		/// <summary>
		/// ABGR as four 16-bit unsigned normalized channels, in that order in memory, with the
		/// bytes within each channel in little-endian order.
		/// </summary>
		Abgr64UnormLe,

		/// <summary>
		/// ABGR as four 16-bit unsigned normalized channels, in that order in memory, with the
		/// bytes within each channel in big-endian order.
		/// </summary>
		Abgr64UnormBe,

		/// <summary>
		/// R as one 8-bit unsigned normalized channel.
		/// </summary>
		/// <remarks>
		/// Can convert to a gray format, since it's exactly one channel.
		/// </remarks>
		R8Unorm,

		/// <summary>
		/// RG as two 8-bit unsigned normalized channels, in that order in memory.
		/// </summary>
		Rg16Unorm,

		/// <summary>
		/// Gray (L) as one 8-bit unsigned normalized channel. Is a gray format.
		/// </summary>
		/// <remarks>
		/// <para>
		/// As with any gray format, converting to this format is disallowed unless the source format has
		/// exactly one channel.
		/// </para>
		/// <para>
		/// "L" stands for "luminance".
		/// </para>
		/// </remarks>
		L8Unorm,

		/// <summary>
		/// Gray and alpha (LA) as two 8-bit unsigned normalized channels, in that order in memory. Is a
		/// gray format.
		/// </summary>
		/// <remarks>
		/// <para>
		/// As with any gray format, converting to this format is disallowed unless the source format has
		/// exactly one channel.
		/// </para>
		/// <para>
		/// "L" stands for "luminance".
		/// </para>
		/// </remarks>
		La16Unorm,

		/// <summary>
		/// RGB as three 8-bit unsigned normalized channels, in that order in memory.
		/// </summary>
		Rgb24Unorm,

		/// <summary>
		/// BGR as three 8-bit unsigned normalized channels, in that order in memory.
		/// </summary>
		Bgr24Unorm,

		/// <summary>
		/// RGB as 5-, 6-, and 5-bit unsigned normalized channels packed into 16 bits from the
		/// most to the least significant bit, with the bytes within the pixel in little-endian order.
		/// </summary>
		Rgb565UnormPack16Le,

		/// <summary>
		/// RGB as 5-, 6-, and 5-bit unsigned normalized channels packed into 16 bits from the
		/// most to the least significant bit, with the bytes within the pixel in big-endian order.
		/// </summary>
		Rgb565UnormPack16Be,

		/// <summary>
		/// RGBA as four 4-bit unsigned normalized channels packed into 16 bits from the
		/// most to the least significant bit, with the bytes within the pixel in little-endian order.
		/// </summary>
		Rgba4444UnormPack16Le,

		/// <summary>
		/// RGBA as four 4-bit unsigned normalized channels packed into 16 bits from the
		/// most to the least significant bit, with the bytes within the pixel in big-endian order.
		/// </summary>
		Rgba4444UnormPack16Be,

		/// <summary>
		/// RGBA as 5-, 5-, 5-, and 1-bit unsigned normalized channels packed into 16 bits from the
		/// most to the least significant bit, with the bytes within the pixel in little-endian order.
		/// </summary>
		Rgba5551UnormPack16Le,

		/// <summary>
		/// RGBA as 5-, 5-, 5-, and 1-bit unsigned normalized channels packed into 16 bits from the
		/// most to the least significant bit, with the bytes within the pixel in big-endian order.
		/// </summary>
		Rgba5551UnormPack16Be,
	}
}

internal enum PixelFormatFamily : byte {
	ByteAligned1x8,
	ByteAligned2x8,
	ByteAligned3x8,
	ByteAligned4x8,
	ByteAligned4x16,
	Packed16,
}

internal enum PixelNumericKind : byte {
	Unorm,
}

internal enum PixelByteOrder : byte {
	NotApplicable,
	LittleEndian,
	BigEndian,
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly struct PixelFormatDesc(
	PixelFormatFamily family,
	PixelNumericKind numericKind,
	PixelByteOrder byteOrder,
	byte bytesPerPixel,
	bool hasR,
	bool hasG,
	bool hasB,
	bool hasA,
	byte rBits,
	byte gBits,
	byte bBits,
	byte aBits,
	byte rShift,
	byte gShift,
	byte bShift,
	byte aShift,
	sbyte rIndex,
	sbyte gIndex,
	sbyte bIndex,
	sbyte aIndex,
	bool isGray = false
) : IHasInvalidDefault {
	public readonly PixelFormatFamily Family = family;
	public readonly PixelNumericKind NumericKind = numericKind;
	public readonly PixelByteOrder ByteOrder = byteOrder;
	public readonly byte BytesPerPixel = bytesPerPixel;

	public readonly bool HasR = hasR;
	public readonly bool HasG = hasG;
	public readonly bool HasB = hasB;
	public readonly bool HasA = hasA;

	public readonly byte RBits = rBits;
	public readonly byte GBits = gBits;
	public readonly byte BBits = bBits;
	public readonly byte ABits = aBits;

	public readonly byte RShift = rShift;
	public readonly byte GShift = gShift;
	public readonly byte BShift = bShift;
	public readonly byte AShift = aShift;

	public readonly sbyte RIndex = rIndex;
	public readonly sbyte GIndex = gIndex;
	public readonly sbyte BIndex = bIndex;
	public readonly sbyte AIndex = aIndex;

	public readonly bool IsGray = isGray;

	bool IHasInvalidDefault.IsDefault => BytesPerPixel == 0;
}
