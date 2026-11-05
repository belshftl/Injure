// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// Higher-level wrapper for a single-layer, non-multisampled, sampleable 2D texture of any
/// non-depth/stencil format. See <see cref="SrgbTexture2d"/> if you're looking for a plain
/// "texture" type.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="SrgbTexture2d"/>, which is for specifically color data, here the contents can
/// be anything (examples: coverage masks, distance fields, motion vectors, integer data), and are
/// not necessarily anything meant to be drawn in the first place, so drawing one with
/// <see cref="Canvas"/> requires passing in a material specifying how to interpret it. (Note:
/// that's currently unimplemented, sorry.)
/// </para>
/// </remarks>
public sealed class RawTexture2d : IDisposable {
	private readonly GpuDevice device;
	private readonly GpuTexture texture;
	private bool disposed = false;

	/// <summary>
	/// The underlying texture, guaranteed to have <see cref="TextureUsage.TextureBinding"/> and
	/// <see cref="TextureUsage.CopyDst"/> usages.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this texture has been disposed.
	/// </exception>
	public GpuTextureRef GpuTexture {
		get {
			chk();
			return texture.AsRef();
		}
	}

	/// <summary>
	/// Width of mip level 0 in texels.
	/// </summary>
	public uint Width => texture.Width;

	/// <summary>
	/// Height of mip level 0 in textureels.
	/// </summary>
	public uint Height => texture.Height;

	/// <summary>
	/// Number of mip levels, including the base level.
	/// </summary>
	public uint MipLevelCount => texture.MipLevelCount;

	/// <summary>
	/// Texture format, which uploaded data has to be in. Always a non-depth/stencil format.
	/// </summary>
	public TextureFormat Format => texture.Format;

	/// <summary>
	/// Creates an empty <see cref="RawTexture2d"/>.
	/// </summary>
	/// <param name="device">Device to create the texture on.</param>
	/// <param name="params">Size, format, and mip level count.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if the width or height is 0, or the mip level count is more than a full chain's worth.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the format is <see cref="TextureFormat.Undefined"/> or a depth/stencil format or
	/// needs a feature the device doesn't have, or if the format is block-compressed and the size
	/// isn't a multiple of its block size.
	/// </exception>
	public RawTexture2d(GpuDevice device, in RawTexture2dParams @params) {
		ArgumentNullException.ThrowIfNull(device);
		ArgumentInvalidDefaultException.ThrowIfDefault(@params, nameof(@params));
		if (@params.Format == TextureFormat.Undefined || @params.Format.IsDepthOrStencil())
			throw new ArgumentException($"format must not be Undefined or a depth/stencil format, got {@params.Format}", nameof(@params));
		this.device = device;
		texture = Texture2dCommon.Create(
			device,
			@params.Width,
			@params.Height,
			@params.Mips.Resolve(@params.Width, @params.Height, nameof(@params)),
			@params.Format,
			TextureUsage.TextureBinding | TextureUsage.CopyDst,
			viewFormat: null
		);
	}

	/// <summary>
	/// Uploads data to a sub-rectangle of a mip level.
	/// </summary>
	/// <param name="mipLevel">Mip level to upload to.</param>
	/// <param name="x">Destination X offset in the level's texels.</param>
	/// <param name="y">Destination Y offset in the level's texels.</param>
	/// <param name="width">Width of the rectangle in the level's texels.</param>
	/// <param name="height">Height of the rectangle in the level's texels.</param>
	/// <param name="data">Texel data in the same format as <see cref="Format"/>.</param>
	/// <param name="bytesBetweenRows">
	/// Bytes from the start of one row in <paramref name="data"/> to the start of the next (rows of
	/// blocks for block-compressed formats); the last row only takes its own texels' worth. See
	/// <see cref="GpuTextureLayout.BytesBetweenRows"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="width"/> or <paramref name="height"/> is 0.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="mipLevel"/> doesn't exist, the rectangle extends past the level or
	/// isn't aligned to the format's blocks, <paramref name="bytesBetweenRows"/> is smaller than a
	/// tightly-packed row of <paramref name="width"/> texels, or <paramref name="data"/> is too small
	/// for the rectangle.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this texture has been disposed.
	/// </exception>
	/// <remarks>
	/// For block-compressed formats, the rectangle's offset and size must be multiples of the block
	/// size, and the rectangle may extend past a small mip level's size up to the next block
	/// boundary. For example, level 3 (1x1) of an 8x8 BC7 texture needs the upload to have a
	/// width/height of 4 (BC7 has 4x4 blocks) and a data size of 16 bytes (one BC7 block is 16
	/// bytes).
	/// </remarks>
	public void UploadToMip(uint mipLevel, uint x, uint y, uint width, uint height, ReadOnlySpan<byte> data, uint bytesBetweenRows) {
		chk();
		Texture2dCommon.Write(device, texture, mipLevel, x, y, width, height, data, bytesBetweenRows);
	}

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		texture.Dispose();
	}

	private void chk() => ObjectDisposedException.ThrowIf(disposed, this);
}

/// <summary>
/// Parameters used to create a <see cref="RawTexture2d"/>.
/// </summary>
/// <param name="Width">Texture width (i.e. width of mip level 0) in texels.</param>
/// <param name="Height">Texture height (i.e. height of mip level 0) in texels.</param>
/// <param name="Format">
/// Texture format; must not be <see cref="TextureFormat.Undefined"/> or a depth/stencil format.
/// Keep in mind that block-compressed formats need the corresponding <see cref="GpuFeatures"/> on
/// the device and a size that's a multiple of the format's block size.
/// </param>
/// <param name="Mips">Mip level count.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its width/height are 0 and its format is
/// <see cref="TextureFormat.Undefined"/>.
/// </remarks>
public readonly record struct RawTexture2dParams(
	uint Width,
	uint Height,
	TextureFormat Format,
	MipLevels Mips = default
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
