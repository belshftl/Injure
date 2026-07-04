// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Draw.PixelConv;

/// <summary>
/// Identifies a pixel storage format.
/// </summary>
/// <remarks>
/// <para>
/// Unless explicitly otherwise noted, pixels are tightly packed with no padding.
/// For example, four RGB24 pixels occupy 12 bytes, not e.g 16.
/// </para>
/// <para>
/// Only storage is described; higher-level semantics such as premultiplied alpha,
/// transfer function, or colorspace are not encoded.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct PixelFormat {
	/// <summary>Raw switch tag for <see cref="PixelFormat"/>.</summary>
	public enum Case {
		/// <summary>
		/// RGBA stored as four 8-bit unsigned normalized channels.
		/// </summary>
		Rgba32_Unorm = 1,

		/// <summary>
		/// BGRA stored as four 8-bit unsigned normalized channels.
		/// </summary>
		Bgra32_Unorm,

		/// <summary>
		/// ARGB stored as four 8-bit unsigned normalized channels.
		/// </summary>
		Argb32_Unorm,

		/// <summary>
		/// ABGR stored as four 8-bit unsigned normalized channels.
		/// </summary>
		Abgr32_Unorm,

		/// <summary>
		/// RGBA stored as four 16-bit unsigned normalized channels in little-endian channel byte order.
		/// </summary>
		Rgba64_Unorm_Le,

		/// <summary>
		/// RGBA stored as four 16-bit unsigned normalized channels in big-endian channel byte order.
		/// </summary>
		Rgba64_Unorm_Be,

		/// <summary>
		/// BGRA stored as four 16-bit unsigned normalized channels in little-endian channel byte order.
		/// </summary>
		Bgra64_Unorm_Le,

		/// <summary>
		/// BGRA stored as four 16-bit unsigned normalized channels in big-endian channel byte order.
		/// </summary>
		Bgra64_Unorm_Be,

		/// <summary>
		/// ARGB stored as four 16-bit unsigned normalized channels in little-endian channel byte order.
		/// </summary>
		Argb64_Unorm_Le,

		/// <summary>
		/// ARGB stored as four 16-bit unsigned normalized channels in big-endian channel byte order.
		/// </summary>
		Argb64_Unorm_Be,

		/// <summary>
		/// ABGR stored as four 16-bit unsigned normalized channels in little-endian channel byte order.
		/// </summary>
		Abgr64_Unorm_Le,

		/// <summary>
		/// ABGR stored as four 16-bit unsigned normalized channels in big-endian channel byte order.
		/// </summary>
		Abgr64_Unorm_Be,

		/// <summary>
		/// A single 8-bit unsigned normalized red channel.
		/// </summary>
		R8_Unorm,

		/// <summary>
		/// Red + green stored as two 8-bit unsigned normalized channels.
		/// </summary>
		Rg16_Unorm,

		/// <summary>
		/// RGB stored as three 8-bit unsigned normalized channels.
		/// </summary>
		Rgb24_Unorm,

		/// <summary>
		/// BGR stored as three 8-bit unsigned normalized channels.
		/// </summary>
		Bgr24_Unorm,

		/// <summary>
		/// BGR stored as three 5:6:5 unsigned normalized channels packed into
		/// 16 bits in little-endian byte order.
		/// </summary>
		Bgr565_UnormPack16_Le,

		/// <summary>
		/// BGR stored as three 5:6:5 unsigned normalized channels packed into
		/// 16 bits in big-endian byte order.
		/// </summary>
		Bgr565_UnormPack16_Be,

		/// <summary>
		/// RGBA stored as four 4-bit unsigned normalized channels packed into
		/// 16 bits in little-endian byte order.
		/// </summary>
		Rgba4444_UnormPack16_Le,

		/// <summary>
		/// RGBA stored as four 4-bit unsigned normalized channels packed into
		/// 16 bits in big-endian byte order.
		/// </summary>
		Rgba4444_UnormPack16_Be,

		/// <summary>
		/// RGBA stored as four 5:5:5:1 unsigned normalized channels packed into
		/// 16 bits in little-endian byte order.
		/// </summary>
		Rgba5551_UnormPack16_Le,

		/// <summary>
		/// RGBA stored as four 5:5:5:1 unsigned normalized channels packed into
		/// 16 bits in big-endian byte order.
		/// </summary>
		Rgba5551_UnormPack16_Be,
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
	sbyte aIndex
) {
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
}
