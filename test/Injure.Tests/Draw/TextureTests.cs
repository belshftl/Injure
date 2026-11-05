// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw;
using Injure.Gpu;
using Injure.Tests.Gpu;

namespace Injure.Tests.Draw;

public sealed class TextureTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private RawTexture2d raw(TextureFormat format, uint size = 4, MipLevels mips = default) =>
		new(Device, new RawTexture2dParams(size, size, format, mips));

	private SrgbTexture2d bc7(uint size, MipLevels mips = default) =>
		new(Device, new SrgbTexture2dParams(size, size, TextureFormat.Bc7RgbaUnormSrgb, mips));

	private void skipWithoutBc() =>
		Assert.SkipUnless(Device.Features.HasAll(GpuFeatures.TextureCompressionBc), "device has no BC texture compression");

	[Fact]
	public void RawTexturesTakeAnyFormatButDepthStencil() {
		using RawTexture2d r8 = raw(TextureFormat.R8Unorm);
		using RawTexture2d f32 = raw(TextureFormat.Rgba32Float);
		using RawTexture2d u32 = raw(TextureFormat.R32Uint);
		using RawTexture2d srgb = raw(TextureFormat.Rgba8UnormSrgb);
		Assert.Throws<ArgumentException>(() => raw(TextureFormat.Depth32Float));
		Assert.Throws<ArgumentException>(() => raw(TextureFormat.Stencil8));
		Assert.Throws<ArgumentException>(() => raw(TextureFormat.Undefined));
	}

	[Fact]
	public void UploadsCheckTheirData() {
		using RawTexture2d tex = raw(TextureFormat.R8Unorm, mips: MipLevels.Count(2));
		tex.UploadToMip(0, 0, 0, 4, 4, new byte[16], 4);
		tex.UploadToMip(1, 0, 0, 2, 2, new byte[4], 2);
		Assert.Throws<ArgumentException>(() => tex.UploadToMip(0, 0, 0, 4, 4, new byte[15], 4));
		Assert.Throws<ArgumentException>(() => tex.UploadToMip(0, 0, 0, 4, 4, ReadOnlySpan<byte>.Empty, 4));
		Assert.Throws<ArgumentException>(() => tex.UploadToMip(0, 0, 0, 4, 4, new byte[16], 3));
		Assert.Throws<ArgumentException>(() => tex.UploadToMip(1, 0, 0, 4, 4, new byte[16], 4));
		Assert.Throws<ArgumentException>(() => tex.UploadToMip(2, 0, 0, 1, 1, new byte[1], 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => tex.UploadToMip(0, 0, 0, 0, 4, new byte[16], 4));
	}

	[Fact]
	public void CompressedTexturesOnlyTakeUploadsToMipLevels() {
		skipWithoutBc();
		// bc7 has 4x4 blocks of 16 bytes, the mip levels are 8x8, 4x4, 2x2, and 1x1
		using SrgbTexture2d chain = bc7(8, MipLevels.FullChain);
		using SrgbTexture2d single = bc7(8);
		Assert.Throws<InvalidOperationException>(() => chain.Upload(new byte[64], 32));
		Assert.Throws<InvalidOperationException>(() => single.Upload(new byte[64], 32));
		Assert.Throws<InvalidOperationException>(chain.GenerateMips);
		Assert.Throws<InvalidOperationException>(single.GenerateMips);
		chain.UploadToMip(0, 0, 0, 8, 8, new byte[64], 32);
		chain.UploadToMip(2, 0, 0, 4, 4, new byte[16], 16);
		Assert.Throws<ArgumentException>(() => chain.UploadToMip(2, 0, 0, 2, 2, new byte[16], 16));
	}

	[Fact]
	public void CompressedTexturesNeedBlockAlignedSizesAndTheirFeature() {
		skipWithoutBc();
		Assert.Throws<ArgumentException>(() => bc7(6));
		using GpuDevice plain = GpuRig.CreateDevice();
		Assert.Throws<ArgumentException>(() => new SrgbTexture2d(plain, new SrgbTexture2dParams(8, 8, TextureFormat.Bc7RgbaUnormSrgb)));
		Assert.Throws<ArgumentException>(() => new RawTexture2d(plain, new RawTexture2dParams(8, 8, TextureFormat.Bc4RUnorm)));
	}
}
