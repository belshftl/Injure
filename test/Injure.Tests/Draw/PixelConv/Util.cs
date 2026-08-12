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
		PixelFormat.Case.Rgba32_Unorm => 4,
		PixelFormat.Case.Bgra32_Unorm => 4,
		PixelFormat.Case.Argb32_Unorm => 4,
		PixelFormat.Case.Abgr32_Unorm => 4,
		PixelFormat.Case.Rgba64_Unorm_Le => 8,
		PixelFormat.Case.Rgba64_Unorm_Be => 8,
		PixelFormat.Case.Bgra64_Unorm_Le => 8,
		PixelFormat.Case.Bgra64_Unorm_Be => 8,
		PixelFormat.Case.Argb64_Unorm_Le => 8,
		PixelFormat.Case.Argb64_Unorm_Be => 8,
		PixelFormat.Case.Abgr64_Unorm_Le => 8,
		PixelFormat.Case.Abgr64_Unorm_Be => 8,
		PixelFormat.Case.R8_Unorm => 1,
		PixelFormat.Case.Rg16_Unorm => 2,
		PixelFormat.Case.Rgb24_Unorm => 3,
		PixelFormat.Case.Bgr24_Unorm => 3,
		PixelFormat.Case.Bgr565_UnormPack16_Le => 2,
		PixelFormat.Case.Bgr565_UnormPack16_Be => 2,
		PixelFormat.Case.Rgba4444_UnormPack16_Le => 2,
		PixelFormat.Case.Rgba4444_UnormPack16_Be => 2,
		PixelFormat.Case.Rgba5551_UnormPack16_Le => 2,
		PixelFormat.Case.Rgba5551_UnormPack16_Be => 2,
		_ => throw new UnreachableException(),
	};

}
