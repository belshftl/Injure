// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Injure.Primitives;

internal static class SrgbTransfer {
	private const int encodeLutMax = 4095;
	// exact, since there are only 256 inputs
	private static readonly ImmutableArray<float> decodeLut8 = buildDecodeLut8();
	// over linear [0, 1] in steps of 1/4095, within one of exact
	private static readonly ImmutableArray<byte> encodeLut8 = buildEncodeLut8();

	public static double Decode(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	public static double Encode(double l) => l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055;

	public static float Decode8(byte v) => decodeLut8[v];
	public static byte Encode8(double l) => (byte)(Math.Clamp(Encode(l), 0.0, 1.0) * 255.0 + 0.5);

	/// <remarks>
	/// Has an error of ±1, but is a lot faster.
	/// </remarks>
	public static byte Encode8Approx(float l) => encodeLut8[(int)(l * encodeLutMax + 0.5f)];

	private static ImmutableArray<float> buildDecodeLut8() {
		float[] lut = new float[256];
		for (int i = 0; i < lut.Length; i++)
			lut[i] = (float)Decode(i / 255.0);
		return ImmutableCollectionsMarshal.AsImmutableArray(lut);
	}

	private static ImmutableArray<byte> buildEncodeLut8() {
		byte[] lut = new byte[encodeLutMax + 1];
		for (int i = 0; i < lut.Length; i++)
			lut[i] = (byte)(Encode(i / (double)encodeLutMax) * 255.0 + 0.5);
		return ImmutableCollectionsMarshal.AsImmutableArray(lut);
	}
}
