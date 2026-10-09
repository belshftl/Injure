// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The format of a texture's texels.
/// </summary>
/// <remarks>
/// <para>
/// Compressed formats (BC, ETC2, EAC, ASTC) and <see cref="Depth32FloatStencil8"/> need the
/// corresponding <see cref="GpuFeatures"/> to be enabled on the device.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>, which is invalid
/// for creating textures.
/// </para>
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUTextureFormat))]
public readonly partial struct TextureFormat {
	/// <summary>Raw switch tag for <see cref="TextureFormat"/>.</summary>
	public enum Case {
		/// <summary>
		/// No format. Invalid for creating textures.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// One 8-bit unsigned normalized channel (R).
		/// </summary>
		R8Unorm = 1,

		/// <summary>
		/// One 8-bit signed normalized channel (R).
		/// </summary>
		R8Snorm = 2,

		/// <summary>
		/// One 8-bit unsigned integer channel (R).
		/// </summary>
		R8Uint = 3,

		/// <summary>
		/// One 8-bit signed integer channel (R).
		/// </summary>
		R8Sint = 4,

		/// <summary>
		/// One 16-bit unsigned integer channel (R).
		/// </summary>
		R16Uint = 5,

		/// <summary>
		/// One 16-bit signed integer channel (R).
		/// </summary>
		R16Sint = 6,

		/// <summary>
		/// One 16-bit float channel (R).
		/// </summary>
		R16Float = 7,

		/// <summary>
		/// Two 8-bit unsigned normalized channels (RG).
		/// </summary>
		Rg8Unorm = 8,

		/// <summary>
		/// Two 8-bit signed normalized channels (RG).
		/// </summary>
		Rg8Snorm = 9,

		/// <summary>
		/// Two 8-bit unsigned integer channels (RG).
		/// </summary>
		Rg8Uint = 10,

		/// <summary>
		/// Two 8-bit signed integer channels (RG).
		/// </summary>
		Rg8Sint = 11,

		/// <summary>
		/// One 32-bit float channel (R).
		/// </summary>
		R32Float = 12,

		/// <summary>
		/// One 32-bit unsigned integer channel (R).
		/// </summary>
		R32Uint = 13,

		/// <summary>
		/// One 32-bit signed integer channel (R).
		/// </summary>
		R32Sint = 14,

		/// <summary>
		/// Two 16-bit unsigned integer channels (RG).
		/// </summary>
		Rg16Uint = 15,

		/// <summary>
		/// Two 16-bit signed integer channels (RG).
		/// </summary>
		Rg16Sint = 16,

		/// <summary>
		/// Two 16-bit float channels (RG).
		/// </summary>
		Rg16Float = 17,

		/// <summary>
		/// Four 8-bit unsigned normalized channels (RGBA).
		/// </summary>
		Rgba8Unorm = 18,

		/// <summary>
		/// Four 8-bit unsigned normalized channels (RGBA), sRGB-encoded.
		/// </summary>
		Rgba8UnormSrgb = 19,

		/// <summary>
		/// Four 8-bit signed normalized channels (RGBA).
		/// </summary>
		Rgba8Snorm = 20,

		/// <summary>
		/// Four 8-bit unsigned integer channels (RGBA).
		/// </summary>
		Rgba8Uint = 21,

		/// <summary>
		/// Four 8-bit signed integer channels (RGBA).
		/// </summary>
		Rgba8Sint = 22,

		/// <summary>
		/// Four 8-bit unsigned normalized channels (BGRA).
		/// </summary>
		Bgra8Unorm = 23,

		/// <summary>
		/// Four 8-bit unsigned normalized channels (BGRA), sRGB-encoded.
		/// </summary>
		Bgra8UnormSrgb = 24,

		/// <summary>
		/// 10-bit red, green, and blue and 2-bit alpha, unsigned integers.
		/// </summary>
		Rgb10a2Uint = 25,

		/// <summary>
		/// 10-bit red, green, and blue and 2-bit alpha, unsigned normalized.
		/// </summary>
		Rgb10a2Unorm = 26,

		/// <summary>
		/// 11-bit red and green, and 10-bit blue unsigned floats.
		/// </summary>
		Rg11b10Ufloat = 27,

		/// <summary>
		/// Red, green, and blue with 9-bit mantissas and a shared 5-bit exponent.
		/// </summary>
		Rgb9e5Ufloat = 28,

		/// <summary>
		/// Two 32-bit float channels (RG).
		/// </summary>
		Rg32Float = 29,

		/// <summary>
		/// Two 32-bit unsigned integer channels (RG).
		/// </summary>
		Rg32Uint = 30,

		/// <summary>
		/// Two 32-bit signed integer channels (RG).
		/// </summary>
		Rg32Sint = 31,

		/// <summary>
		/// Four 16-bit unsigned integer channels (RGBA).
		/// </summary>
		Rgba16Uint = 32,

		/// <summary>
		/// Four 16-bit signed integer channels (RGBA).
		/// </summary>
		Rgba16Sint = 33,

		/// <summary>
		/// Four 16-bit float channels (RGBA).
		/// </summary>
		Rgba16Float = 34,

		/// <summary>
		/// Four 32-bit float channels (RGBA).
		/// </summary>
		Rgba32Float = 35,

		/// <summary>
		/// Four 32-bit unsigned integer channels (RGBA).
		/// </summary>
		Rgba32Uint = 36,

		/// <summary>
		/// Four 32-bit signed integer channels (RGBA).
		/// </summary>
		Rgba32Sint = 37,

		/// <summary>8-bit stencil.</summary>
		Stencil8 = 38,

		/// <summary>
		/// 16-bit unsigned normalized depth.
		/// </summary>
		Depth16Unorm = 39,

		/// <summary>
		/// Depth with at least 24 bits of precision; the exact format depends on the device, so it can't
		/// be copied.
		/// </summary>
		Depth24Plus = 40,

		/// <summary>
		/// <see cref="Depth24Plus"/> depth and 8-bit stencil.
		/// </summary>
		Depth24PlusStencil8 = 41,

		/// <summary>32-bit float depth.</summary>
		Depth32Float = 42,

		/// <summary>
		/// 32-bit float depth and 8-bit stencil. Requires <see cref="GpuFeatures.Depth32FloatStencil8"/>.
		/// </summary>
		Depth32FloatStencil8 = 43,

		/// <summary>
		/// BC1-compressed RGBA, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc1RgbaUnorm = 44,

		/// <summary>
		/// BC1-compressed RGBA, unsigned normalized, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc1RgbaUnormSrgb = 45,

		/// <summary>
		/// BC2-compressed RGBA, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc2RgbaUnorm = 46,

		/// <summary>
		/// BC2-compressed RGBA, unsigned normalized, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc2RgbaUnormSrgb = 47,

		/// <summary>
		/// BC3-compressed RGBA, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc3RgbaUnorm = 48,

		/// <summary>
		/// BC3-compressed RGBA, unsigned normalized, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc3RgbaUnormSrgb = 49,

		/// <summary>
		/// BC4-compressed R, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc4RUnorm = 50,

		/// <summary>
		/// BC4-compressed R, signed normalized. Requires <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc4RSnorm = 51,

		/// <summary>
		/// BC5-compressed RG, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc5RgUnorm = 52,

		/// <summary>
		/// BC5-compressed RG, signed normalized. Requires <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc5RgSnorm = 53,

		/// <summary>
		/// BC6H-compressed RGB, unsigned float. Requires <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc6hRgbUfloat = 54,

		/// <summary>
		/// BC6H-compressed RGB, signed float. Requires <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc6hRgbFloat = 55,

		/// <summary>
		/// BC7-compressed RGBA, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc7RgbaUnorm = 56,

		/// <summary>
		/// BC7-compressed RGBA, unsigned normalized, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionBc"/>.
		/// </summary>
		Bc7RgbaUnormSrgb = 57,

		/// <summary>
		/// ETC2-compressed RGB, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		Etc2Rgb8Unorm = 58,

		/// <summary>
		/// ETC2-compressed RGB, unsigned normalized, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		Etc2Rgb8UnormSrgb = 59,

		/// <summary>
		/// ETC2-compressed RGB with 1-bit alpha, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		Etc2Rgb8a1Unorm = 60,

		/// <summary>
		/// ETC2-compressed RGB with 1-bit alpha, unsigned normalized, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		Etc2Rgb8a1UnormSrgb = 61,

		/// <summary>
		/// ETC2-compressed RGBA, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		Etc2Rgba8Unorm = 62,

		/// <summary>
		/// ETC2-compressed RGBA, unsigned normalized, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		Etc2Rgba8UnormSrgb = 63,

		/// <summary>
		/// EAC-compressed R, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		EacR11Unorm = 64,

		/// <summary>
		/// EAC-compressed R, signed normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		EacR11Snorm = 65,

		/// <summary>
		/// EAC-compressed RG, unsigned normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		EacRg11Unorm = 66,

		/// <summary>
		/// EAC-compressed RG, signed normalized. Requires
		/// <see cref="GpuFeatures.TextureCompressionEtc2"/>.
		/// </summary>
		EacRg11Snorm = 67,

		/// <summary>
		/// ASTC-compressed RGBA with 4x4 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc4x4Unorm = 68,

		/// <summary>
		/// ASTC-compressed RGBA with 4x4 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc4x4UnormSrgb = 69,

		/// <summary>
		/// ASTC-compressed RGBA with 5x4 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc5x4Unorm = 70,

		/// <summary>
		/// ASTC-compressed RGBA with 5x4 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc5x4UnormSrgb = 71,

		/// <summary>
		/// ASTC-compressed RGBA with 5x5 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc5x5Unorm = 72,

		/// <summary>
		/// ASTC-compressed RGBA with 5x5 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc5x5UnormSrgb = 73,

		/// <summary>
		/// ASTC-compressed RGBA with 6x5 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc6x5Unorm = 74,

		/// <summary>
		/// ASTC-compressed RGBA with 6x5 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc6x5UnormSrgb = 75,

		/// <summary>
		/// ASTC-compressed RGBA with 6x6 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc6x6Unorm = 76,

		/// <summary>
		/// ASTC-compressed RGBA with 6x6 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc6x6UnormSrgb = 77,

		/// <summary>
		/// ASTC-compressed RGBA with 8x5 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc8x5Unorm = 78,

		/// <summary>
		/// ASTC-compressed RGBA with 8x5 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc8x5UnormSrgb = 79,

		/// <summary>
		/// ASTC-compressed RGBA with 8x6 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc8x6Unorm = 80,

		/// <summary>
		/// ASTC-compressed RGBA with 8x6 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc8x6UnormSrgb = 81,

		/// <summary>
		/// ASTC-compressed RGBA with 8x8 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc8x8Unorm = 82,

		/// <summary>
		/// ASTC-compressed RGBA with 8x8 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc8x8UnormSrgb = 83,

		/// <summary>
		/// ASTC-compressed RGBA with 10x5 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc10x5Unorm = 84,

		/// <summary>
		/// ASTC-compressed RGBA with 10x5 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc10x5UnormSrgb = 85,

		/// <summary>
		/// ASTC-compressed RGBA with 10x6 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc10x6Unorm = 86,

		/// <summary>
		/// ASTC-compressed RGBA with 10x6 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc10x6UnormSrgb = 87,

		/// <summary>
		/// ASTC-compressed RGBA with 10x8 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc10x8Unorm = 88,

		/// <summary>
		/// ASTC-compressed RGBA with 10x8 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc10x8UnormSrgb = 89,

		/// <summary>
		/// ASTC-compressed RGBA with 10x10 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc10x10Unorm = 90,

		/// <summary>
		/// ASTC-compressed RGBA with 10x10 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc10x10UnormSrgb = 91,

		/// <summary>
		/// ASTC-compressed RGBA with 12x10 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc12x10Unorm = 92,

		/// <summary>
		/// ASTC-compressed RGBA with 12x10 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc12x10UnormSrgb = 93,

		/// <summary>
		/// ASTC-compressed RGBA with 12x12 blocks. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc12x12Unorm = 94,

		/// <summary>
		/// ASTC-compressed RGBA with 12x12 blocks, sRGB-encoded. Requires
		/// <see cref="GpuFeatures.TextureCompressionAstc"/>.
		/// </summary>
		Astc12x12UnormSrgb = 95,
	}

}

/// <summary>
/// sRGB-related helpers for <see cref="TextureFormat"/>.
/// </summary>
public static class TextureFormatSrgbExtensions {
	/// <summary>
	/// Whether <paramref name="format"/> is an sRGB-encoded format, i.e. one that decodes to linear on reads and encodes
	/// from linear on writes.
	/// </summary>
	public static bool IsSrgb(this TextureFormat format) => format.ToNonSrgb() != format;

	/// <summary>
	/// Returns the non-sRGB counterpart of <paramref name="format"/> if it's sRGB-encoded (e.g.
	/// <see cref="TextureFormat.Rgba8Unorm"/> for <see cref="TextureFormat.Rgba8UnormSrgb"/>), and
	/// <paramref name="format"/> otherwise.
	/// </summary>
	/// <remarks>
	/// A format and its counterpart can be listed as each other's view formats.
	/// </remarks>
	public static TextureFormat ToNonSrgb(this TextureFormat format) => format.Tag switch {
		TextureFormat.Case.Rgba8UnormSrgb => TextureFormat.Rgba8Unorm,
		TextureFormat.Case.Bgra8UnormSrgb => TextureFormat.Bgra8Unorm,
		TextureFormat.Case.Bc1RgbaUnormSrgb => TextureFormat.Bc1RgbaUnorm,
		TextureFormat.Case.Bc2RgbaUnormSrgb => TextureFormat.Bc2RgbaUnorm,
		TextureFormat.Case.Bc3RgbaUnormSrgb => TextureFormat.Bc3RgbaUnorm,
		TextureFormat.Case.Bc7RgbaUnormSrgb => TextureFormat.Bc7RgbaUnorm,
		TextureFormat.Case.Etc2Rgb8UnormSrgb => TextureFormat.Etc2Rgb8Unorm,
		TextureFormat.Case.Etc2Rgb8a1UnormSrgb => TextureFormat.Etc2Rgb8a1Unorm,
		TextureFormat.Case.Etc2Rgba8UnormSrgb => TextureFormat.Etc2Rgba8Unorm,
		TextureFormat.Case.Astc4x4UnormSrgb => TextureFormat.Astc4x4Unorm,
		TextureFormat.Case.Astc5x4UnormSrgb => TextureFormat.Astc5x4Unorm,
		TextureFormat.Case.Astc5x5UnormSrgb => TextureFormat.Astc5x5Unorm,
		TextureFormat.Case.Astc6x5UnormSrgb => TextureFormat.Astc6x5Unorm,
		TextureFormat.Case.Astc6x6UnormSrgb => TextureFormat.Astc6x6Unorm,
		TextureFormat.Case.Astc8x5UnormSrgb => TextureFormat.Astc8x5Unorm,
		TextureFormat.Case.Astc8x6UnormSrgb => TextureFormat.Astc8x6Unorm,
		TextureFormat.Case.Astc8x8UnormSrgb => TextureFormat.Astc8x8Unorm,
		TextureFormat.Case.Astc10x5UnormSrgb => TextureFormat.Astc10x5Unorm,
		TextureFormat.Case.Astc10x6UnormSrgb => TextureFormat.Astc10x6Unorm,
		TextureFormat.Case.Astc10x8UnormSrgb => TextureFormat.Astc10x8Unorm,
		TextureFormat.Case.Astc10x10UnormSrgb => TextureFormat.Astc10x10Unorm,
		TextureFormat.Case.Astc12x10UnormSrgb => TextureFormat.Astc12x10Unorm,
		TextureFormat.Case.Astc12x12UnormSrgb => TextureFormat.Astc12x12Unorm,
		_ => format,
	};

	/// <summary>
	/// Returns the sRGB counterpart of <paramref name="format"/> if it has one (e.g.
	/// <see cref="TextureFormat.Rgba8UnormSrgb"/> for <see cref="TextureFormat.Rgba8Unorm"/>), and
	/// <paramref name="format"/> otherwise.
	/// </summary>
	/// <remarks>
	/// A format and its counterpart can be listed as each other's view formats.
	/// </remarks>
	public static TextureFormat ToSrgb(this TextureFormat format) => format.Tag switch {
		TextureFormat.Case.Rgba8Unorm => TextureFormat.Rgba8UnormSrgb,
		TextureFormat.Case.Bgra8Unorm => TextureFormat.Bgra8UnormSrgb,
		TextureFormat.Case.Bc1RgbaUnorm => TextureFormat.Bc1RgbaUnormSrgb,
		TextureFormat.Case.Bc2RgbaUnorm => TextureFormat.Bc2RgbaUnormSrgb,
		TextureFormat.Case.Bc3RgbaUnorm => TextureFormat.Bc3RgbaUnormSrgb,
		TextureFormat.Case.Bc7RgbaUnorm => TextureFormat.Bc7RgbaUnormSrgb,
		TextureFormat.Case.Etc2Rgb8Unorm => TextureFormat.Etc2Rgb8UnormSrgb,
		TextureFormat.Case.Etc2Rgb8a1Unorm => TextureFormat.Etc2Rgb8a1UnormSrgb,
		TextureFormat.Case.Etc2Rgba8Unorm => TextureFormat.Etc2Rgba8UnormSrgb,
		TextureFormat.Case.Astc4x4Unorm => TextureFormat.Astc4x4UnormSrgb,
		TextureFormat.Case.Astc5x4Unorm => TextureFormat.Astc5x4UnormSrgb,
		TextureFormat.Case.Astc5x5Unorm => TextureFormat.Astc5x5UnormSrgb,
		TextureFormat.Case.Astc6x5Unorm => TextureFormat.Astc6x5UnormSrgb,
		TextureFormat.Case.Astc6x6Unorm => TextureFormat.Astc6x6UnormSrgb,
		TextureFormat.Case.Astc8x5Unorm => TextureFormat.Astc8x5UnormSrgb,
		TextureFormat.Case.Astc8x6Unorm => TextureFormat.Astc8x6UnormSrgb,
		TextureFormat.Case.Astc8x8Unorm => TextureFormat.Astc8x8UnormSrgb,
		TextureFormat.Case.Astc10x5Unorm => TextureFormat.Astc10x5UnormSrgb,
		TextureFormat.Case.Astc10x6Unorm => TextureFormat.Astc10x6UnormSrgb,
		TextureFormat.Case.Astc10x8Unorm => TextureFormat.Astc10x8UnormSrgb,
		TextureFormat.Case.Astc10x10Unorm => TextureFormat.Astc10x10UnormSrgb,
		TextureFormat.Case.Astc12x10Unorm => TextureFormat.Astc12x10UnormSrgb,
		TextureFormat.Case.Astc12x12Unorm => TextureFormat.Astc12x12UnormSrgb,
		_ => format,
	};
}
