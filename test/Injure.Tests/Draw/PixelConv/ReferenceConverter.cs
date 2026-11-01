// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Injure.Draw.PixelConv;

namespace Injure.Tests.Draw.PixelConv;

public enum ReferenceFamily {
	Copy32SetAlpha,
	Copy64SetAlpha,
	Shuffle32,
	Expand24To32,
	Contract32To24,
	Widen32To64,
	Narrow64To32,
	Packed16To32,
	Unpacked32ToPacked16,
	Generic,
}

public static class ReferenceConverter {
	private readonly struct Rgb24Layout(int rIndex, int gIndex, int bIndex) {
		public readonly int RIndex = rIndex, GIndex = gIndex, BIndex = bIndex;
	}

	private readonly struct Rgba32Layout(int rIndex, int gIndex, int bIndex, int aIndex) {
		public readonly int RIndex = rIndex, GIndex = gIndex, BIndex = bIndex, AIndex = aIndex;
	}

	private readonly struct Rgba64Layout(int rIndex, int gIndex, int bIndex, int aIndex, bool isBigEndian) {
		public readonly int RIndex = rIndex, GIndex = gIndex, BIndex = bIndex, AIndex = aIndex;
		public readonly bool IsBigEndian = isBigEndian;
	}

	private readonly struct Packed16Layout(
		int rBits,
		int gBits,
		int bBits,
		int aBits,
		int rShift,
		int gShift,
		int bShift,
		int aShift,
		bool isBigEndian
	) {
		public readonly int RBits = rBits, GBits = gBits, BBits = bBits, ABits = aBits;
		public readonly int RShift = rShift, GShift = gShift, BShift = bShift, AShift = aShift;
		public readonly bool IsBigEndian = isBigEndian;
	}

	private static void validate(ReadOnlySpan<byte> src, int srcStride, int srcRowBytes, int width, int height) {
		ArgumentOutOfRangeException.ThrowIfNegative(width);
		ArgumentOutOfRangeException.ThrowIfNegative(height);
		ArgumentOutOfRangeException.ThrowIfLessThan(srcStride, srcRowBytes);
		ArgumentOutOfRangeException.ThrowIfLessThan(src.Length, height == 0 ? 0 : checked((height - 1) * srcStride + srcRowBytes));
	}

	public static byte[] Convert(
		ReferenceFamily family,
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		in PixelFormatConvertOptions opts
	) {
		return family switch {
			ReferenceFamily.Copy32SetAlpha => Copy32SetAlpha(src, srcStride, srcFmt, width, height, opts.Alpha16Unorm),
			ReferenceFamily.Copy64SetAlpha => Copy64SetAlpha(src, srcStride, srcFmt, width, height, opts.Alpha16Unorm),
			ReferenceFamily.Shuffle32 => Shuffle32(src, srcStride, srcFmt, dstFmt, width, height, opts.Alpha16Unorm, opts.OverrideAlpha),
			ReferenceFamily.Expand24To32 => Expand24To32(src, srcStride, srcFmt, dstFmt, width, height, opts.Alpha16Unorm),
			ReferenceFamily.Contract32To24 => Contract32To24(src, srcStride, srcFmt, dstFmt, width, height),
			ReferenceFamily.Widen32To64 => Widen32To64(src, srcStride, srcFmt, dstFmt, width, height, opts.Alpha16Unorm, opts.OverrideAlpha),
			ReferenceFamily.Narrow64To32 => Narrow64To32(src, srcStride, srcFmt, dstFmt, width, height, opts.Alpha16Unorm, opts.OverrideAlpha),
			ReferenceFamily.Packed16To32 => Packed16To32(src, srcStride, srcFmt, dstFmt, width, height, opts.Alpha16Unorm, opts.OverrideAlpha),
			ReferenceFamily.Unpacked32ToPacked16 => Unpacked32ToPacked16(src, srcStride, srcFmt, dstFmt, width, height, opts.Alpha16Unorm, opts.OverrideAlpha),
			ReferenceFamily.Generic => Generic(src, srcStride, srcFmt, dstFmt, width, height, opts.Alpha16Unorm, opts.OverrideAlpha),
			_ => throw new UnreachableException(),
		};
	}

	public static byte[] Copy32SetAlpha(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat fmt,
		int width,
		int height,
		ushort a16unorm
	) {
		Rgba32Layout sl = rgba32LayoutFor(fmt);
		int rowBytes = checked(width * 4);
		validate(src, srcStride, rowBytes, width, height);

		byte[] dst = new byte[checked(rowBytes * height)];
		byte a8 = narrow16To8(a16unorm);
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, rowBytes);
			Span<byte> d = dst.AsSpan(y * rowBytes, rowBytes);
			s.CopyTo(d);
			for (int x = 0; x < width; x++)
				d[x * 4 + sl.AIndex] = a8;
		}
		return dst;
	}

	public static byte[] Copy64SetAlpha(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat fmt,
		int width,
		int height,
		ushort a16unorm
	) {
		Rgba64Layout sl = rgba64LayoutFor(fmt);
		int rowBytes = checked(width * 8);
		validate(src, srcStride, rowBytes, width, height);

		byte[] dst = new byte[checked(rowBytes * height)];
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, rowBytes);
			Span<byte> d = dst.AsSpan(y * rowBytes, rowBytes);
			s.CopyTo(d);
			for (int x = 0; x < width; x++)
				writeU16(d, x * 8 + sl.AIndex * 2, sl.IsBigEndian, a16unorm);
		}
		return dst;
	}

	public static byte[] Shuffle32(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		ushort a16unorm,
		bool overrideAlpha
	) {
		Rgba32Layout sl = rgba32LayoutFor(srcFmt);
		Rgba32Layout dl = rgba32LayoutFor(dstFmt);
		int srcRowBytes = checked(width * 4);
		int dstRowBytes = checked(width * 4);
		validate(src, srcStride, srcRowBytes, width, height);

		byte[] dst = new byte[checked(dstRowBytes * height)];
		byte a8 = narrow16To8(a16unorm);
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, srcRowBytes);
			Span<byte> d = dst.AsSpan(y * dstRowBytes, dstRowBytes);

			for (int x = 0; x < width; x++) {
				int roff = x * 4;
				int woff = x * 4;

				byte r = s[roff + sl.RIndex];
				byte g = s[roff + sl.GIndex];
				byte b = s[roff + sl.BIndex];
				byte a = overrideAlpha ? a8 : s[roff + sl.AIndex];

				d[woff + dl.RIndex] = r;
				d[woff + dl.GIndex] = g;
				d[woff + dl.BIndex] = b;
				d[woff + dl.AIndex] = a;
			}
		}
		return dst;
	}

	public static byte[] Expand24To32(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		ushort a16unorm
	) {
		Rgb24Layout sl = rgb24LayoutFor(srcFmt);
		Rgba32Layout dl = rgba32LayoutFor(dstFmt);
		int srcRowBytes = checked(width * 3);
		int dstRowBytes = checked(width * 4);
		validate(src, srcStride, srcRowBytes, width, height);

		byte[] dst = new byte[checked(dstRowBytes * height)];
		byte a8 = narrow16To8(a16unorm);
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, srcRowBytes);
			Span<byte> d = dst.AsSpan(y * dstRowBytes, dstRowBytes);

			for (int x = 0; x < width; x++) {
				int roff = x * 3;
				int woff = x * 4;

				d[woff + dl.RIndex] = s[roff + sl.RIndex];
				d[woff + dl.GIndex] = s[roff + sl.GIndex];
				d[woff + dl.BIndex] = s[roff + sl.BIndex];
				d[woff + dl.AIndex] = a8;
			}
		}
		return dst;
	}

	public static byte[] Contract32To24(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height
	) {
		Rgba32Layout sl = rgba32LayoutFor(srcFmt);
		Rgb24Layout dl = rgb24LayoutFor(dstFmt);
		int srcRowBytes = checked(width * 4);
		int dstRowBytes = checked(width * 3);
		validate(src, srcStride, srcRowBytes, width, height);

		byte[] dst = new byte[checked(dstRowBytes * height)];
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, srcRowBytes);
			Span<byte> d = dst.AsSpan(y * dstRowBytes, dstRowBytes);

			for (int x = 0; x < width; x++) {
				int roff = x * 4;
				int woff = x * 3;

				d[woff + dl.RIndex] = s[roff + sl.RIndex];
				d[woff + dl.GIndex] = s[roff + sl.GIndex];
				d[woff + dl.BIndex] = s[roff + sl.BIndex];
			}
		}
		return dst;
	}

	public static byte[] Widen32To64(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		ushort a16unorm,
		bool overrideAlpha
	) {
		Rgba32Layout sl = rgba32LayoutFor(srcFmt);
		Rgba64Layout dl = rgba64LayoutFor(dstFmt);
		int srcRowBytes = checked(width * 4);
		int dstRowBytes = checked(width * 8);
		validate(src, srcStride, srcRowBytes, width, height);

		byte[] dst = new byte[checked(dstRowBytes * height)];
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, srcRowBytes);
			Span<byte> d = dst.AsSpan(y * dstRowBytes, dstRowBytes);

			for (int x = 0; x < width; x++) {
				int roff = x * 4;
				int woff = x * 8;

				ushort r = widen8To16(s[roff + sl.RIndex]);
				ushort g = widen8To16(s[roff + sl.GIndex]);
				ushort b = widen8To16(s[roff + sl.BIndex]);
				ushort a = overrideAlpha ? a16unorm : widen8To16(s[roff + sl.AIndex]);

				writeU16(d, woff + dl.RIndex * 2, dl.IsBigEndian, r);
				writeU16(d, woff + dl.GIndex * 2, dl.IsBigEndian, g);
				writeU16(d, woff + dl.BIndex * 2, dl.IsBigEndian, b);
				writeU16(d, woff + dl.AIndex * 2, dl.IsBigEndian, a);
			}
		}
		return dst;
	}

	public static byte[] Narrow64To32(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		ushort a16unorm,
		bool overrideAlpha
	) {
		Rgba64Layout sl = rgba64LayoutFor(srcFmt);
		Rgba32Layout dl = rgba32LayoutFor(dstFmt);
		int srcRowBytes = checked(width * 8);
		int dstRowBytes = checked(width * 4);
		validate(src, srcStride, srcRowBytes, width, height);

		byte[] dst = new byte[checked(dstRowBytes * height)];
		byte a8 = narrow16To8(a16unorm);
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, srcRowBytes);
			Span<byte> d = dst.AsSpan(y * dstRowBytes, dstRowBytes);
			for (int x = 0; x < width; x++) {
				int roff = x * 8;
				int woff = x * 4;

				byte r = narrow16To8(readU16(s, roff + sl.RIndex * 2, sl.IsBigEndian));
				byte g = narrow16To8(readU16(s, roff + sl.GIndex * 2, sl.IsBigEndian));
				byte b = narrow16To8(readU16(s, roff + sl.BIndex * 2, sl.IsBigEndian));
				byte a = overrideAlpha ? a8 : narrow16To8(readU16(s, roff + sl.AIndex * 2, sl.IsBigEndian));

				d[woff + dl.RIndex] = r;
				d[woff + dl.GIndex] = g;
				d[woff + dl.BIndex] = b;
				d[woff + dl.AIndex] = a;
			}
		}
		return dst;
	}

	public static byte[] Packed16To32(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		ushort a16unorm,
		bool overrideAlpha
	) {
		Packed16Layout sl = packed16LayoutFor(srcFmt);
		Rgba32Layout dl = rgba32LayoutFor(dstFmt);
		int srcRowBytes = checked(width * 2);
		int dstRowBytes = checked(width * 4);
		validate(src, srcStride, srcRowBytes, width, height);

		byte[] dst = new byte[checked(dstRowBytes * height)];
		byte a8 = narrow16To8(a16unorm);
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, srcRowBytes);
			Span<byte> d = dst.AsSpan(y * dstRowBytes, dstRowBytes);
			for (int x = 0; x < width; x++) {
				ushort packed = readU16(s, x * 2, sl.IsBigEndian);
				byte r = scaleNTo8((uint)packed >> sl.RShift & bitmask(sl.RBits), sl.RBits);
				byte g = scaleNTo8((uint)packed >> sl.GShift & bitmask(sl.GBits), sl.GBits);
				byte b = scaleNTo8((uint)packed >> sl.BShift & bitmask(sl.BBits), sl.BBits);
				byte a = sl.ABits == 0 ? a8 : scaleNTo8((uint)packed >> sl.AShift & bitmask(sl.ABits), sl.ABits);
				if (overrideAlpha)
					a = a8;
				int woff = x * 4;
				d[woff + dl.RIndex] = r;
				d[woff + dl.GIndex] = g;
				d[woff + dl.BIndex] = b;
				d[woff + dl.AIndex] = a;
			}
		}
		return dst;
	}

	public static byte[] Unpacked32ToPacked16(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		ushort a16unorm,
		bool overrideAlpha
	) {
		Rgba32Layout sl = rgba32LayoutFor(srcFmt);
		Packed16Layout dl = packed16LayoutFor(dstFmt);
		int srcRowBytes = checked(width * 4);
		int dstRowBytes = checked(width * 2);
		validate(src, srcStride, srcRowBytes, width, height);

		byte[] dst = new byte[checked(dstRowBytes * height)];
		byte a8 = narrow16To8(a16unorm);
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, srcRowBytes);
			Span<byte> d = dst.AsSpan(y * dstRowBytes, dstRowBytes);
			for (int x = 0; x < width; x++) {
				int roff = x * 4;

				byte r = s[roff + sl.RIndex];
				byte g = s[roff + sl.GIndex];
				byte b = s[roff + sl.BIndex];
				byte a = overrideAlpha ? a8 : s[roff + sl.AIndex];

				uint packed = 0;
				packed |= scale8ToN(r, dl.RBits) << dl.RShift;
				packed |= scale8ToN(g, dl.GBits) << dl.GShift;
				packed |= scale8ToN(b, dl.BBits) << dl.BShift;
				if (dl.ABits != 0)
					packed |= scale8ToN(a, dl.ABits) << dl.AShift;

				writeU16(d, x * 2, dl.IsBigEndian, (ushort)packed);
			}
		}
		return dst;
	}

	// every channel goes through 16-bit unorm; missing color channels decode as 0, missing alpha
	// decodes as the fill alpha, gray decodes into all three color channels, and encoding gray
	// takes R
	public static byte[] Generic(
		ReadOnlySpan<byte> src,
		int srcStride,
		PixelFormat srcFmt,
		PixelFormat dstFmt,
		int width,
		int height,
		ushort a16unorm,
		bool overrideAlpha
	) {
		int srcBpp = Util.GetBytesPerPixel(srcFmt);
		int dstBpp = Util.GetBytesPerPixel(dstFmt);
		int srcRowBytes = checked(width * srcBpp);
		int dstRowBytes = checked(width * dstBpp);
		validate(src, srcStride, srcRowBytes, width, height);

		byte[] dst = new byte[checked(dstRowBytes * height)];
		for (int y = 0; y < height; y++) {
			ReadOnlySpan<byte> s = src.Slice(y * srcStride, srcRowBytes);
			Span<byte> d = dst.AsSpan(y * dstRowBytes, dstRowBytes);
			for (int x = 0; x < width; x++) {
				decode16(s.Slice(x * srcBpp, srcBpp), srcFmt, a16unorm, out ushort r, out ushort g, out ushort b, out ushort a);
				if (overrideAlpha)
					a = a16unorm;
				encode16(d.Slice(x * dstBpp, dstBpp), dstFmt, r, g, b, a);
			}
		}
		return dst;
	}

	private static void decode16(ReadOnlySpan<byte> s, PixelFormat fmt, ushort a16unorm, out ushort r, out ushort g, out ushort b, out ushort a) {
		r = g = b = 0;
		a = a16unorm;
		switch (Util.GetBytesPerPixel(fmt)) {
		case 1:
			r = widen8To16(s[0]);
			if (fmt == PixelFormat.L8Unorm)
				g = b = r;
			return;
		case 2 when fmt == PixelFormat.Rg16Unorm:
			r = widen8To16(s[0]);
			g = widen8To16(s[1]);
			return;
		case 2 when fmt == PixelFormat.La16Unorm:
			r = g = b = widen8To16(s[0]);
			a = widen8To16(s[1]);
			return;
		case 2:
			Packed16Layout pl = packed16LayoutFor(fmt);
			ushort packed = readU16(s, 0, pl.IsBigEndian);
			r = scaleNTo16((uint)packed >> pl.RShift & bitmask(pl.RBits), pl.RBits);
			g = scaleNTo16((uint)packed >> pl.GShift & bitmask(pl.GBits), pl.GBits);
			b = scaleNTo16((uint)packed >> pl.BShift & bitmask(pl.BBits), pl.BBits);
			if (pl.ABits != 0)
				a = scaleNTo16((uint)packed >> pl.AShift & bitmask(pl.ABits), pl.ABits);
			return;
		case 3:
			Rgb24Layout l3 = rgb24LayoutFor(fmt);
			r = widen8To16(s[l3.RIndex]);
			g = widen8To16(s[l3.GIndex]);
			b = widen8To16(s[l3.BIndex]);
			return;
		case 4:
			Rgba32Layout l4 = rgba32LayoutFor(fmt);
			r = widen8To16(s[l4.RIndex]);
			g = widen8To16(s[l4.GIndex]);
			b = widen8To16(s[l4.BIndex]);
			a = widen8To16(s[l4.AIndex]);
			return;
		case 8:
			Rgba64Layout l8 = rgba64LayoutFor(fmt);
			r = readU16(s, l8.RIndex * 2, l8.IsBigEndian);
			g = readU16(s, l8.GIndex * 2, l8.IsBigEndian);
			b = readU16(s, l8.BIndex * 2, l8.IsBigEndian);
			a = readU16(s, l8.AIndex * 2, l8.IsBigEndian);
			return;
		default:
			throw new UnreachableException();
		}
	}

	private static void encode16(Span<byte> d, PixelFormat fmt, ushort r, ushort g, ushort b, ushort a) {
		switch (Util.GetBytesPerPixel(fmt)) {
		case 1:
			d[0] = narrow16To8(r);
			return;
		case 2 when fmt == PixelFormat.Rg16Unorm:
			d[0] = narrow16To8(r);
			d[1] = narrow16To8(g);
			return;
		case 2 when fmt == PixelFormat.La16Unorm:
			d[0] = narrow16To8(r);
			d[1] = narrow16To8(a);
			return;
		case 2:
			Packed16Layout pl = packed16LayoutFor(fmt);
			uint packed = 0;
			packed |= scale16ToN(r, pl.RBits) << pl.RShift;
			packed |= scale16ToN(g, pl.GBits) << pl.GShift;
			packed |= scale16ToN(b, pl.BBits) << pl.BShift;
			if (pl.ABits != 0)
				packed |= scale16ToN(a, pl.ABits) << pl.AShift;
			writeU16(d, 0, pl.IsBigEndian, (ushort)packed);
			return;
		case 3:
			Rgb24Layout l3 = rgb24LayoutFor(fmt);
			d[l3.RIndex] = narrow16To8(r);
			d[l3.GIndex] = narrow16To8(g);
			d[l3.BIndex] = narrow16To8(b);
			return;
		case 4:
			Rgba32Layout l4 = rgba32LayoutFor(fmt);
			d[l4.RIndex] = narrow16To8(r);
			d[l4.GIndex] = narrow16To8(g);
			d[l4.BIndex] = narrow16To8(b);
			d[l4.AIndex] = narrow16To8(a);
			return;
		case 8:
			Rgba64Layout l8 = rgba64LayoutFor(fmt);
			writeU16(d, l8.RIndex * 2, l8.IsBigEndian, r);
			writeU16(d, l8.GIndex * 2, l8.IsBigEndian, g);
			writeU16(d, l8.BIndex * 2, l8.IsBigEndian, b);
			writeU16(d, l8.AIndex * 2, l8.IsBigEndian, a);
			return;
		default:
			throw new UnreachableException();
		}
	}

	private static Rgb24Layout rgb24LayoutFor(PixelFormat fmt) => fmt.Tag switch {
		PixelFormat.Case.Rgb24Unorm => new Rgb24Layout(0, 1, 2),
		PixelFormat.Case.Bgr24Unorm => new Rgb24Layout(2, 1, 0),

		_ => throw new ArgumentOutOfRangeException(nameof(fmt)),
	};

	private static Rgba32Layout rgba32LayoutFor(PixelFormat fmt) => fmt.Tag switch {
		PixelFormat.Case.Rgba32Unorm => new Rgba32Layout(0, 1, 2, 3),
		PixelFormat.Case.Bgra32Unorm => new Rgba32Layout(2, 1, 0, 3),
		PixelFormat.Case.Argb32Unorm => new Rgba32Layout(1, 2, 3, 0),
		PixelFormat.Case.Abgr32Unorm => new Rgba32Layout(3, 2, 1, 0),

		_ => throw new ArgumentOutOfRangeException(nameof(fmt)),
	};

	private static Rgba64Layout rgba64LayoutFor(PixelFormat fmt) => fmt.Tag switch {
		PixelFormat.Case.Rgba64UnormLe => new Rgba64Layout(0, 1, 2, 3, false),
		PixelFormat.Case.Bgra64UnormLe => new Rgba64Layout(2, 1, 0, 3, false),
		PixelFormat.Case.Argb64UnormLe => new Rgba64Layout(1, 2, 3, 0, false),
		PixelFormat.Case.Abgr64UnormLe => new Rgba64Layout(3, 2, 1, 0, false),

		PixelFormat.Case.Rgba64UnormBe => new Rgba64Layout(0, 1, 2, 3, true),
		PixelFormat.Case.Bgra64UnormBe => new Rgba64Layout(2, 1, 0, 3, true),
		PixelFormat.Case.Argb64UnormBe => new Rgba64Layout(1, 2, 3, 0, true),
		PixelFormat.Case.Abgr64UnormBe => new Rgba64Layout(3, 2, 1, 0, true),

		_ => throw new ArgumentOutOfRangeException(nameof(fmt)),
	};

	private static Packed16Layout packed16LayoutFor(PixelFormat fmt) => fmt.Tag switch {
		PixelFormat.Case.Rgb565UnormPack16Le => new Packed16Layout(5, 6, 5, 0, 11, 5, 0, 0, false),
		PixelFormat.Case.Rgba4444UnormPack16Le => new Packed16Layout(4, 4, 4, 4, 12, 8, 4, 0, false),
		PixelFormat.Case.Rgba5551UnormPack16Le => new Packed16Layout(5, 5, 5, 1, 11, 6, 1, 0, false),

		PixelFormat.Case.Rgb565UnormPack16Be => new Packed16Layout(5, 6, 5, 0, 11, 5, 0, 0, true),
		PixelFormat.Case.Rgba4444UnormPack16Be => new Packed16Layout(4, 4, 4, 4, 12, 8, 4, 0, true),
		PixelFormat.Case.Rgba5551UnormPack16Be => new Packed16Layout(5, 5, 5, 1, 11, 6, 1, 0, true),

		_ => throw new ArgumentOutOfRangeException(nameof(fmt)),
	};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ushort readU16(ReadOnlySpan<byte> src, int offset, bool bigEndian) =>
		bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(src.Slice(offset, 2)) : BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(offset, 2));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void writeU16(Span<byte> dst, int offset, bool bigEndian, ushort value) {
		if (bigEndian)
			BinaryPrimitives.WriteUInt16BigEndian(dst.Slice(offset, 2), value);
		else
			BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(offset, 2), value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ushort widen8To16(byte b) => (ushort)(b << 8 | b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static byte narrow16To8(ushort s) => (byte)((s * 0xffu + 0x7fffu) / 0xffffu);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static uint bitmask(int bits) {
		if (bits <= 0)
			return 0;
		if (bits >= 32)
			return uint.MaxValue;
		return (1u << bits) - 1u;
	}

	private static byte scaleNTo8(uint val, int bits) {
		if (bits <= 0)
			return 0;
		if (bits == 8)
			return (byte)val;
		uint max = bitmask(bits);
		return (byte)((val * 0xffu + (max >> 1)) / max);
	}

	private static uint scale8ToN(byte val, int bits) {
		if (bits <= 0)
			return 0;
		if (bits == 8)
			return val;
		uint max = bitmask(bits);
		return (val * max + 0x7fu) / 0xffu;
	}

	private static ushort scaleNTo16(uint val, int bits) {
		if (bits <= 0)
			return 0;
		if (bits == 16)
			return (ushort)val;
		uint max = bitmask(bits);
		return (ushort)((val * 0xffffu + (max >> 1)) / max);
	}

	private static uint scale16ToN(ushort val, int bits) {
		if (bits <= 0)
			return 0;
		if (bits == 16)
			return val;
		uint max = bitmask(bits);
		return (val * max + 0x7fffu) / 0xffffu;
	}
}
