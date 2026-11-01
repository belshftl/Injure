// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.PixelConv;
using static Injure.Tests.Draw.PixelConv.Util;

namespace Injure.Tests.Draw.PixelConv;

public static class BasicConversionTests {
	[Fact]
	public static void BasicShufflingWorks() {
		PixelFormatConversionPlan plan = PixelFormatConverter.CreatePlan(PixelFormat.Rgba32Unorm, PixelFormat.Bgra32Unorm, default);
		byte[] src = [0x10, 0x20, 0x30, 0x40, 0xa0, 0xb0, 0xc0, 0xd0];
		byte[] dst = new byte[src.Length];
		plan.ConvertRow(src, dst, pxCount: 2);
		Assert.Equal([0x30, 0x20, 0x10, 0x40, 0xc0, 0xb0, 0xa0, 0xd0], dst);
	}

	[Fact]
	public static void AlphaOverrideWorks() {
		PixelFormatConversionPlan plan = PixelFormatConverter.CreatePlan(
			PixelFormat.Rgba32Unorm,
			PixelFormat.Bgra32Unorm,
			new PixelFormatConvertOptions(alpha16Unorm: 0x8080, overrideAlpha: true)
		);
		byte[] src = [
			0x01, 0x02, 0x03, 0x04,
			0x11, 0x12, 0x13, 0x14,
			0x21, 0x22, 0x23, 0x24,
			0x31, 0x32, 0x33, 0x34,
			0x41, 0x42, 0x43, 0x44,
		];
		byte[] dst = new byte[src.Length];
		plan.ConvertRow(src, dst, pxCount: 5);
		Assert.Equal(
			[
				0x03, 0x02, 0x01, 0x80,
				0x13, 0x12, 0x11, 0x80,
				0x23, 0x22, 0x21, 0x80,
				0x33, 0x32, 0x31, 0x80,
				0x43, 0x42, 0x41, 0x80,
			],
			dst
		);
	}

	[Fact]
	public static void FillingMissingChannelsWorks() {
		PixelFormatConversionPlan plan = PixelFormatConverter.CreatePlan(PixelFormat.R8Unorm, PixelFormat.Rgba32Unorm, default);
		byte[] src = [0x10, 0x20];
		byte[] dst = new byte[src.Length * 4];
		plan.ConvertRow(src, dst, pxCount: 2);
		Assert.Equal([0x10, 0x00, 0x00, 0xff, 0x20, 0x00, 0x00, 0xff], dst);
	}

	[Fact]
	public static void GrayIsWrittenIntoEveryColorChannel() {
		PixelFormatConversionPlan plan = PixelFormatConverter.CreatePlan(PixelFormat.La16Unorm, PixelFormat.Rgba32Unorm, default);
		byte[] src = [0x10, 0x20, 0xa0, 0xb0];
		byte[] dst = new byte[8];
		plan.ConvertRow(src, dst, pxCount: 2);
		Assert.Equal([0x10, 0x10, 0x10, 0x20, 0xa0, 0xa0, 0xa0, 0xb0], dst);
	}

	[Fact]
	public static void ColorToGrayIsRefusedUnlessTheSourceIsOneChannel() {
		PixelFormatConvertOptions all = new(flags: PixelFormatConversionFlags.AllowNarrowing | PixelFormatConversionFlags.AllowDroppingChannels);
		Assert.Throws<ArgumentException>(() => PixelFormatConverter.CreatePlan(PixelFormat.Rgba32Unorm, PixelFormat.La16Unorm, all));
		Assert.Throws<ArgumentException>(() => PixelFormatConverter.CreatePlan(PixelFormat.Rg16Unorm, PixelFormat.L8Unorm, all));

		PixelFormatConversionPlan plan = PixelFormatConverter.CreatePlan(PixelFormat.R8Unorm, PixelFormat.L8Unorm, default);
		byte[] dst = new byte[2];
		plan.ConvertRow([0x12, 0x34], dst, pxCount: 2);
		Assert.Equal([0x12, 0x34], dst);
	}

	[Fact]
	public static void DroppingAndNarrowingWorks() {
		Assert.Throws<ArgumentException>(() => _ = PixelFormatConverter.CreatePlan(PixelFormat.Rgba64UnormLe, PixelFormat.R8Unorm, default));

		PixelFormatConversionPlan plan = PixelFormatConverter.CreatePlan(
			PixelFormat.Rgba64UnormLe,
			PixelFormat.R8Unorm,
			new PixelFormatConvertOptions(flags: PixelFormatConversionFlags.AllowNarrowing | PixelFormatConversionFlags.AllowDroppingChannels)
		);
		byte[] src = Rgba64Le((0x1111, 0x2222, 0x3333, 0x4444), (0xaaaa, 0xbbbb, 0xcccc, 0xdddd));
		byte[] dst = new byte[2];
		plan.ConvertRow(src, dst, pxCount: 2);
		Assert.Equal([0x11, 0xaa], dst);
	}

	[Fact]
	public static void OptionsDerivedFromTheDefaultKeepTheirChanges() {
		PixelFormatConvertOptions opts = default(PixelFormatConvertOptions) with { Flags = PixelFormatConversionFlags.AllowNarrowing };
		PixelFormatConversionPlan plan = PixelFormatConverter.CreatePlan(PixelFormat.Rgba64UnormLe, PixelFormat.Rgba32Unorm, opts);
		Assert.Equal(opts, plan.Options);
	}

	[Fact]
	public static void StrideWorks() {
		byte[] src = [
			0x10, 0x20, 0x30, 0x40, 0x00, 0x00,
			0xa0, 0xb0, 0xc0, 0xd0, 0x00, 0x00,
		];
		byte[] dst = new byte[8];
		PixelFormatConverter.Convert(
			src,
			srcStride: 6,
			srcFmt: PixelFormat.Rgba32Unorm,
			dst,
			dstStride: 4,
			dstFmt: PixelFormat.Rgba32Unorm,
			width: 1,
			height: 2,
			opts: default
		);
		Assert.Equal([0x10, 0x20, 0x30, 0x40, 0xa0, 0xb0, 0xc0, 0xd0], dst);
	}
}
