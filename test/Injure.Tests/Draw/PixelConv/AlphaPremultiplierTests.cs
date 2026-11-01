// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.PixelConv;

namespace Injure.Tests.Draw.PixelConv;

public static class AlphaPremultiplierTests {
	private static double srgbToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	private static double linearToSrgb(double l) => l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055;

	// every 8-bit (color, alpha) pair, with the color in R/G and its complement in B
	private static byte[] everyPair8() {
		byte[] px = new byte[256 * 256 * 4];
		for (int c = 0; c < 256; c++) {
			for (int a = 0; a < 256; a++) {
				int i = (c * 256 + a) * 4;
				px[i + 0] = (byte)c;
				px[i + 1] = (byte)c;
				px[i + 2] = (byte)(0xff - c);
				px[i + 3] = (byte)a;
			}
		}
		return px;
	}

	[Fact]
	public static void Linear8IsExact() {
		byte[] px = everyPair8();
		AlphaPremultiplier.Premultiply(px, 256 * 4, PixelFormat.Rgba32Unorm, 256, 256, TransferFunction.Linear);
		for (int c = 0; c < 256; c++) {
			for (int a = 0; a < 256; a++) {
				int i = (c * 256 + a) * 4;
				Assert.Equal((byte)Math.Round(c * a / 255.0), px[i + 0]);
				Assert.Equal((byte)Math.Round((255 - c) * a / 255.0), px[i + 2]);
				Assert.Equal((byte)a, px[i + 3]);
			}
		}
	}

	[Fact]
	public static void Srgb8IsWithinOneOfExact() {
		byte[] px = everyPair8();
		AlphaPremultiplier.Premultiply(px, 256 * 4, PixelFormat.Rgba32Unorm, 256, 256, TransferFunction.Srgb);
		for (int c = 0; c < 256; c++) {
			for (int a = 0; a < 256; a++) {
				int i = (c * 256 + a) * 4;
				int exact = (int)(linearToSrgb(srgbToLinear(c / 255.0) * (a / 255.0)) * 255.0 + 0.5);
				// opaque input shouldn't be touched
				if (a == 0xff)
					Assert.Equal((byte)c, px[i + 0]);
				else
					Assert.InRange(px[i + 0], exact - 1, exact + 1);
				Assert.Equal((byte)a, px[i + 3]);
			}
		}
	}

	[Fact]
	public static void Srgb16IsExact() {
		ushort[] cs = [0, 1, 0x0101, 0x1225, 0x1234, 0x8000, 0xaaaa, 0xbeef, 0xfffe, 0xffff];
		ushort[] alphas = [0, 1, 0x10, 0x4000, 0x8000, 0xfffe, 0xffff];
		List<(ushort R, ushort G, ushort B, ushort A)> pixels = new();
		foreach (ushort c in cs)
			foreach (ushort a in alphas)
				pixels.Add((c, c, c, a));
		byte[] px = Util.Rgba64Le(pixels.ToArray());
		AlphaPremultiplier.Premultiply(px, px.Length, PixelFormat.Rgba64UnormLe, pixels.Count, 1, TransferFunction.Srgb);
		for (int i = 0; i < pixels.Count; i++) {
			(ushort c, _, _, ushort a) = pixels[i];
			ushort exact = (ushort)(linearToSrgb(srgbToLinear(c / 65535.0) * (a / 65535.0)) * 65535.0 + 0.5);
			Assert.Equal(exact, BitConverter.ToUInt16(px, i * 8));
			Assert.Equal(a, BitConverter.ToUInt16(px, i * 8 + 6));
		}
	}

	[Fact]
	public static void ChannelLayoutsAreRespected() {
		// alpha first
		byte[] argb = [0x80, 0xff, 0x80, 0x00];
		AlphaPremultiplier.Premultiply(argb, 4, PixelFormat.Argb32Unorm, 1, 1, TransferFunction.Linear);
		Assert.Equal([0x80, 0x80, 0x40, 0x00], argb);

		// only the gray channel is multiplied
		byte[] la = [0x80, 0x80];
		AlphaPremultiplier.Premultiply(la, 2, PixelFormat.La16Unorm, 1, 1, TransferFunction.Linear);
		Assert.Equal([0x40, 0x80], la);

		// packed: r = 15, g = 8, b = 0, a = 8 of 15
		byte[] packed = [0x08, 0xf8];
		AlphaPremultiplier.Premultiply(packed, 2, PixelFormat.Rgba4444UnormPack16Le, 1, 1, TransferFunction.Linear);
		Assert.Equal([0x08, 0x84], packed);
	}

	[Fact]
	public static void RowPaddingIsLeftAlone() {
		byte[] px = [
			0xff, 0xff, 0xff, 0x80, 0x11, 0x22,
			0xff, 0xff, 0xff, 0x80, 0x33, 0x44,
		];
		AlphaPremultiplier.Premultiply(px, 6, PixelFormat.Rgba32Unorm, 1, 2, TransferFunction.Linear);
		Assert.Equal(
			[
				0x80, 0x80, 0x80, 0x80, 0x11, 0x22,
				0x80, 0x80, 0x80, 0x80, 0x33, 0x44,
			],
			px
		);
	}

	[Fact]
	public static void FormatsWithoutAlphaAreRejected() {
		byte[] px = new byte[3];
		Assert.Throws<ArgumentException>(() => AlphaPremultiplier.Premultiply(px, 3, PixelFormat.Rgb24Unorm, 1, 1, TransferFunction.Linear));
	}
}
