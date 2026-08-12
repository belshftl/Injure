// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.PixelConv;
using static Injure.Tests.Draw.PixelConv.Util;

namespace Injure.Tests.Draw.PixelConv;

public sealed class BasicConversionTests {
	[Fact]
	public void BasicShufflingWorks() {
		PixelConversionPlan plan = PixelConverter.CreatePlan(PixelFormat.Rgba32_Unorm, PixelFormat.Bgra32_Unorm);
		byte[] src = [0x10, 0x20, 0x30, 0x40, 0xa0, 0xb0, 0xc0, 0xd0];
		byte[] dst = new byte[src.Length];
		plan.ConvertRow(src, dst, pxCount: 2);
		Assert.Equal([0x30, 0x20, 0x10, 0x40, 0xc0, 0xb0, 0xa0, 0xd0], dst);
	}

	[Fact]
	public void AlphaOverrideWorks() {
		PixelConversionPlan plan = PixelConverter.CreatePlan(
			PixelFormat.Rgba32_Unorm,
			PixelFormat.Bgra32_Unorm,
			new PixelConvertOptions(Alpha16Unorm: 0x8080, OverrideAlpha: true)
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
	public void FillingMissingChannelsWorks() {
		PixelConversionPlan plan = PixelConverter.CreatePlan(PixelFormat.R8_Unorm, PixelFormat.Rgba32_Unorm);
		byte[] src = [0x10, 0x20];
		byte[] dst = new byte[src.Length * 4];
		plan.ConvertRow(src, dst, pxCount: 2);
		Assert.Equal([0x10, 0x00, 0x00, 0xff, 0x20, 0x00, 0x00, 0xff], dst);
	}

	[Fact]
	public void DroppingAndNarrowingWorks() {
		Assert.Throws<InvalidOperationException>(() => _ = PixelConverter.CreatePlan(PixelFormat.Rgba64_Unorm_Le, PixelFormat.R8_Unorm));

		PixelConversionPlan plan = PixelConverter.CreatePlan(
			PixelFormat.Rgba64_Unorm_Le,
			PixelFormat.R8_Unorm,
			new PixelConvertOptions(Flags: ConversionFlags.AllowNarrowing | ConversionFlags.AllowDroppingChannels)
		);
		byte[] src = Rgba64Le((0x1111, 0x2222, 0x3333, 0x4444), (0xaaaa, 0xbbbb, 0xcccc, 0xdddd));
		byte[] dst = new byte[2];
		plan.ConvertRow(src, dst, pxCount: 2);
		Assert.Equal([0x11, 0xaa], dst);
	}

	[Fact]
	public void StrideWorks() {
		byte[] src = [
			0x10, 0x20, 0x30, 0x40, 0x00, 0x00,
			0xa0, 0xb0, 0xc0, 0xd0, 0x00, 0x00,
		];
		byte[] dst = new byte[8];
		PixelConverter.Convert(
			src,
			srcStride: 6,
			srcFmt: PixelFormat.Rgba32_Unorm,
			dst,
			dstStride: 4,
			dstFmt: PixelFormat.Rgba32_Unorm,
			width: 1,
			height: 2
		);
		Assert.Equal([0x10, 0x20, 0x30, 0x40, 0xa0, 0xb0, 0xc0, 0xd0], dst);
	}
}
