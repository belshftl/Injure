// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Injure.Draw.PixelConv;

namespace Injure.Tests.Draw.PixelConv;

public static class Util {
	public static byte[] Rgba64Le(params (ushort R, ushort G, ushort B, ushort A)[] pixels) {
		static void writeU16Le(byte[] dst, int offset, ushort val) {
			dst[offset + 0] = (byte)val;
			dst[offset + 1] = (byte)(val >> 8);
		}

		byte[] result = new byte[pixels.Length * 8];
		for (int i = 0; i < pixels.Length; i++) {
			int b = i * 8;
			writeU16Le(result, b + 0, pixels[i].R);
			writeU16Le(result, b + 2, pixels[i].G);
			writeU16Le(result, b + 4, pixels[i].B);
			writeU16Le(result, b + 6, pixels[i].A);
		}
		return result;
	}

	public static int GetBytesPerPixel(PixelFormat fmt) => fmt.Tag switch {
		PixelFormat.Case.Rgba32Unorm => 4,
		PixelFormat.Case.Bgra32Unorm => 4,
		PixelFormat.Case.Argb32Unorm => 4,
		PixelFormat.Case.Abgr32Unorm => 4,
		PixelFormat.Case.Rgba64UnormLe => 8,
		PixelFormat.Case.Rgba64UnormBe => 8,
		PixelFormat.Case.Bgra64UnormLe => 8,
		PixelFormat.Case.Bgra64UnormBe => 8,
		PixelFormat.Case.Argb64UnormLe => 8,
		PixelFormat.Case.Argb64UnormBe => 8,
		PixelFormat.Case.Abgr64UnormLe => 8,
		PixelFormat.Case.Abgr64UnormBe => 8,
		PixelFormat.Case.R8Unorm => 1,
		PixelFormat.Case.Rg16Unorm => 2,
		PixelFormat.Case.L8Unorm => 1,
		PixelFormat.Case.La16Unorm => 2,
		PixelFormat.Case.Rgb24Unorm => 3,
		PixelFormat.Case.Bgr24Unorm => 3,
		PixelFormat.Case.Rgb565UnormPack16Le => 2,
		PixelFormat.Case.Rgb565UnormPack16Be => 2,
		PixelFormat.Case.Rgba4444UnormPack16Le => 2,
		PixelFormat.Case.Rgba4444UnormPack16Be => 2,
		PixelFormat.Case.Rgba5551UnormPack16Le => 2,
		PixelFormat.Case.Rgba5551UnormPack16Be => 2,
		_ => throw new UnreachableException(),
	};

}
