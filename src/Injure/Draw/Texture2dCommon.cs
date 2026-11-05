// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;

namespace Injure.Draw;

internal static class Texture2dCommon {
	// GpuDevice.CreateTexture checks sizes, features, and block alignment; the callers only check
	// which formats they allow
	public static GpuTexture Create(
		GpuDevice device,
		uint width,
		uint height,
		uint mipLevelCount,
		TextureFormat format,
		TextureUsage usage,
		TextureFormat? viewFormat
	) => device.CreateTexture(
		new GpuTextureParams(
			Width: width,
			Height: height,
			DepthOrArrayLayers: 1,
			MipLevelCount: mipLevelCount,
			SampleCount: SampleCount.One,
			Dimension: TextureDimension.Dimension2d,
			Format: format,
			Usage: usage,
			ViewFormats: viewFormat is TextureFormat vf ? [vf] : []
		)
	);

	public static void Write(
		GpuDevice device,
		GpuTexture texture,
		uint mipLevel,
		uint x,
		uint y,
		uint width,
		uint height,
		ReadOnlySpan<byte> data,
		uint bytesBetweenRows
	) {
		ArgumentOutOfRangeException.ThrowIfZero(width);
		ArgumentOutOfRangeException.ThrowIfZero(height);
		(_, uint bh) = texture.Format.BlockSize();
		GpuTextureRegion region = new(x, y, 0, width, height, 1, mipLevel, TextureAspect.All);
		GpuTextureLayout layout = new(0, bytesBetweenRows, Math.Max(1, height / bh));
		device.WriteToTexture(texture, region, data, layout);
	}
}
