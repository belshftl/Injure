// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;

namespace Injure.Tests.Gpu;

public sealed class GpuTextureTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private GpuDevice dev => Device;

	[Fact]
	public void CreateTextureReportsItsParameters() {
		using GpuTexture tex = dev.CreateTexture(new GpuTextureCreateParams(
			8, 4, 3, 2, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.TextureBinding | TextureUsage.CopyDst,
			[TextureFormat.Rgba8UnormSrgb]
		));
		Assert.Equal((8u, 4u, 3u, 2u, 1u), (tex.Width, tex.Height, tex.DepthOrArrayLayers, tex.MipLevelCount, tex.SampleCount));
		Assert.Equal(TextureDimension.Dimension2d, tex.Dimension);
		Assert.Equal(TextureViewDimension.Dimension2dArray, tex.DefaultViewDimension);
		Assert.Equal([TextureFormat.Rgba8UnormSrgb], tex.ViewFormats.ToArray());
		Assert.True(tex.SameTexture(tex.AsRef()));

		GpuTextureViewRef view = tex.DefaultView;
		Assert.Equal(TextureViewDimension.Dimension2dArray, view.Dimension);
		Assert.Equal((2u, 3u), (view.MipLevelCount, view.ArrayLayerCount));
		Assert.Equal(GpuRig.Format, view.Format);
	}

	[Fact]
	public void SingleLayer2dTexturesDefaultTo2dViews() {
		using GpuTexture tex = GpuRig.Target(dev, 4, 4);
		Assert.Equal(TextureViewDimension.Dimension2d, tex.DefaultViewDimension);
		Assert.Equal(TextureViewDimension.Dimension2d, tex.DefaultView.Dimension);
	}

	[Fact]
	public void CreateTextureRejectsBadViewFormats() {
		Assert.Throws<ArgumentException>(() => dev.CreateTexture(new GpuTextureCreateParams(
			4, 4, 1, 1, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.TextureBinding, [GpuRig.Format]
		)));
		Assert.Throws<ArgumentException>(() => dev.CreateTexture(new GpuTextureCreateParams(
			4, 4, 1, 1, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.TextureBinding,
			[TextureFormat.Rgba8UnormSrgb, TextureFormat.Rgba8UnormSrgb]
		)));
	}

	[Fact]
	public void ViewsCoverTheRequestedSubresources() {
		using GpuTexture tex = dev.CreateTexture(new GpuTextureCreateParams(
			16, 16, 4, 3, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.TextureBinding
		));
		using GpuTextureView view = tex.CreateView(new GpuTextureViewCreateParams(
			null, TextureViewDimension.Dimension2d, TextureAspect.All, BaseMipLevel: 1, MipLevelCount: 1, BaseArrayLayer: 2
		));
		Assert.Equal((1u, 1u, 2u, 1u), (view.BaseMipLevel, view.MipLevelCount, view.BaseArrayLayer, view.ArrayLayerCount));
		Assert.Equal((8u, 8u), (view.Width, view.Height));
	}

	[Fact]
	public void AspectViewsOfDepthStencilPickAspectFormats() {
		using GpuTexture tex = dev.CreateTexture(new GpuTextureCreateParams(
			4, 4, 1, 1, 1, TextureDimension.Dimension2d, TextureFormat.Depth24PlusStencil8, TextureUsage.TextureBinding
		));
		using GpuTextureView depth = tex.CreateView(new GpuTextureViewCreateParams(null, null, TextureAspect.DepthOnly));
		using GpuTextureView stencil = tex.CreateView(new GpuTextureViewCreateParams(null, null, TextureAspect.StencilOnly));
		Assert.Equal(TextureFormat.Depth24Plus, depth.Format);
		Assert.Equal(TextureFormat.Stencil8, stencil.Format);
		Assert.Throws<ArgumentException>(() => tex.CreateView(default));

		using GpuTexture color = GpuRig.Target(dev, 4, 4);
		Assert.Throws<ArgumentException>(() => color.CreateView(new GpuTextureViewCreateParams(null, null, TextureAspect.DepthOnly)));
	}

	[Fact]
	public void QueueWritesToTextureRespectRowPitch() {
		using GpuTexture tex = dev.CreateTexture(new GpuTextureCreateParams(
			2, 2, 1, 1, 1, TextureDimension.Dimension2d, GpuRig.Format, TextureUsage.CopySrc | TextureUsage.CopyDst
		));
		// 12-byte row pitch, i.e. 4 bytes of padding per row, which queue writes allow
		byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 0, 0, 0, 0, 9, 10, 11, 12, 13, 14, 15, 16, 0, 0, 0, 0];
		dev.WriteToTexture(tex, GpuRig.Whole(tex), data.AsSpan(), new GpuTextureLayout(0, 12, 2));
		Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, GpuRig.Read(dev, tex));
	}

	[Fact]
	public void SamplerCreationValidatesParameters() {
		using GpuSampler ok = dev.CreateSampler(SamplerStates.LinearRepeat);
		Assert.Throws<ArgumentOutOfRangeException>(() => dev.CreateSampler(SamplerStates.LinearClamp with { LodMinClamp = -1 }));
		Assert.Throws<ArgumentOutOfRangeException>(() => dev.CreateSampler(SamplerStates.LinearClamp with { LodMinClamp = 4, LodMaxClamp = 2 }));
		Assert.Throws<ArgumentOutOfRangeException>(() => dev.CreateSampler(SamplerStates.LinearClamp with { MaxAnisotropy = 0 }));
		Assert.Throws<ArgumentException>(() => dev.CreateSampler(SamplerStates.NearestClamp with { MaxAnisotropy = 4 }));
		using GpuSampler aniso = dev.CreateSampler(SamplerStates.LinearClamp with { MaxAnisotropy = 4 });
	}
}
