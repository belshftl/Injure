// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using static Injure.Draw.PixelConv.ConverterCore;

namespace Injure.Draw.PixelConv;

/// <summary>
/// Premultiplies pixel data. Un-premultiplying is currently not implemented.
/// </summary>
/// <remarks>
/// <para>
/// For <see cref="TransferFunction.Srgb"/>, color channels are decoded to linear first,
/// premultiplied, and encoded back to sRGB afterwards, so the result is the sRGB encoding of the
/// premultiplied linear color.
/// </para>
/// <para>
/// Results are rounded to the nearest value, except for 8-bit channels with
/// <see cref="TransferFunction.Srgb"/>, which are within one of the nearest value due to internal
/// optimizations.
/// </para>
/// </remarks>
public static unsafe class AlphaPremultiplier {
	private const int encodeLutMax = 4095;
	private static readonly ImmutableArray<float> decodeLut8 = buildDecodeLut8();
	private static readonly ImmutableArray<byte> encodeLut8 = buildEncodeLut8();

	/// <summary>
	/// Premultiplies a 2D image in place.
	/// </summary>
	/// <param name="pixels">Image to premultiply.</param>
	/// <param name="stride">Row stride in bytes (not pixels).</param>
	/// <param name="fmt">Pixel format of <paramref name="pixels"/>.</param>
	/// <param name="width">Width of the image in pixels.</param>
	/// <param name="height">Height of the image in pixels.</param>
	/// <param name="tf">Transfer function of the color channels.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="stride"/>, <paramref name="width"/>, or <paramref name="height"/> is
	/// negative.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="fmt"/> has no alpha, <paramref name="stride"/> is smaller than a
	/// tightly packed row of <paramref name="width"/> pixels, or <paramref name="pixels"/> is too
	/// small for the image.
	/// </exception>
	public static void Premultiply(
		Span<byte> pixels,
		int stride,
		PixelFormat fmt,
		int width,
		int height,
		TransferFunction tf
	) {
		ArgumentInvalidDefaultException.ThrowIfDefault(fmt);
		ArgumentInvalidDefaultException.ThrowIfDefault(tf);
		ArgumentOutOfRangeException.ThrowIfNegative(stride);
		ArgumentOutOfRangeException.ThrowIfNegative(width);
		ArgumentOutOfRangeException.ThrowIfNegative(height);
		PixelFormatDesc desc = DescOf(fmt);
		if (!desc.HasA)
			throw new ArgumentException("format has no alpha", nameof(fmt));
		if (width == 0 || height == 0)
			return;

		int rowBytes = checked(width * desc.BytesPerPixel);
		if (stride < rowBytes)
			throw new ArgumentException("stride is smaller than the row size", nameof(stride));
		int need = checked((height - 1) * stride + rowBytes);
		if (pixels.Length < need)
			throw new ArgumentException($"pixels is too small (expected at least {need} bytes, got {pixels.Length})", nameof(pixels));

		bool bytes = desc.Family == PixelFormatFamily.ByteAligned4x8
			|| desc.Family == PixelFormatFamily.ByteAligned2x8 && desc.IsGray;
		bool srgb = tf == TransferFunction.Srgb;
		fixed (byte* p = pixels) {
			for (int y = 0; y < height; y++) {
				byte* row = p + (nint)y * stride;
				if (bytes)
					premultiplyRow8(row, in desc, (nuint)width, srgb);
				else
					premultiplyRow16(row, in desc, (nuint)width, srgb);
			}
		}
	}

	private static void premultiplyRow8(byte* row, in PixelFormatDesc desc, nuint width, bool srgb) {
		for (nuint i = 0; i < width; i++) {
			byte* px = row + i * desc.BytesPerPixel;
			byte a = px[desc.AIndex];
			if (a == 0xff)
				continue;
			px[desc.RIndex] = mul8(px[desc.RIndex], a, srgb);
			if (!desc.IsGray) {
				px[desc.GIndex] = mul8(px[desc.GIndex], a, srgb);
				px[desc.BIndex] = mul8(px[desc.BIndex], a, srgb);
			}
		}
	}

	private static void premultiplyRow16(byte* row, in PixelFormatDesc desc, nuint width, bool srgb) {
		for (nuint i = 0; i < width; i++) {
			byte* px = row + i * desc.BytesPerPixel;
			DecodePixel(px, in desc, 0xffff, out ushort r, out ushort g, out ushort b, out ushort a);
			if (a == 0xffff)
				continue;
			EncodePixel(px, in desc, mul16(r, a, srgb), mul16(g, a, srgb), mul16(b, a, srgb), a);
		}
	}

	private static byte mul8(byte c, byte a, bool srgb) {
		if (!srgb)
			return (byte)((c * a + 0x7fu) / 0xffu);
		float lin = decodeLut8[c] * a * (1f / 0xff);
		return encodeLut8[(int)(lin * encodeLutMax + 0.5f)];
	}

	private static ushort mul16(ushort c, ushort a, bool srgb) {
		if (!srgb)
			return (ushort)(((uint)c * a + 0x7fffu) / 0xffffu);
		double lin = srgbToLinear(c / 65535.0) * (a / 65535.0);
		return (ushort)(linearToSrgb(lin) * 65535.0 + 0.5);
	}

	private static double srgbToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	private static double linearToSrgb(double l) => l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055;

	private static ImmutableArray<float> buildDecodeLut8() {
		float[] lut = new float[256];
		for (int i = 0; i < lut.Length; i++)
			lut[i] = (float)srgbToLinear(i / 255.0);
		return ImmutableCollectionsMarshal.AsImmutableArray(lut);
	}

	private static ImmutableArray<byte> buildEncodeLut8() {
		byte[] lut = new byte[encodeLutMax + 1];
		for (int i = 0; i < lut.Length; i++)
			lut[i] = (byte)(linearToSrgb(i / (double)encodeLutMax) * 255.0 + 0.5);
		return ImmutableCollectionsMarshal.AsImmutableArray(lut);
	}
}
