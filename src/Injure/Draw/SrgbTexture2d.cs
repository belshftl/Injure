// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Assets;
using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// Higher-level wrapper for a single-layer, non-multisampled, sampleable 2D texture of
/// specifically an sRGB color format. This is the plain "texture" type, if that's what you're
/// looking for.
/// </summary>
/// <remarks>
/// <para>
/// "sRGB" refers to only the transfer function; see <see cref="TextureFormat"/>.
/// </para>
/// <para>
/// Since <see cref="Canvas"/> deals in premultiplied values, all textures drawn there must be
/// premultiplied. Therefore, the contents of an <see cref="SrgbTexture2d"/> are, in practice,
/// always premultiplied; uploads must supply correctly premultiplied data, and readback gives
/// premultiplied values. (Note: readback for <see cref="SrgbTexture2d"/> is currently
/// unimplemented.)
/// </para>
/// <para>
/// Multi-mip textures are supported, including automatic generation of mip levels from the base
/// level. Currently, it's implemented as a simple box downsample; something better, like
/// Kaiser/Lanczos, is under consideration.
/// </para>
/// <para>
/// A texture loaded through an <see cref="AssetStore"/> is owned by the store until the store
/// reclaims it (see <see cref="IsAssetStoreOwned"/>), and disposing it before that throws. Once
/// reclaimed, members that use the texture throw <see cref="ObjectDisposedException"/>, like after
/// any other disposal.
/// </para>
/// </remarks>
public sealed class SrgbTexture2d : IAssetStoreDisposable {
	private readonly GpuDevice device;
	private readonly GpuTexture texture;
	private GpuBindGroup? bindGroup = null;
	private bool disposed = false;
	private AssetStoreCustody? custody;

	internal GpuBindGroupRef BindGroup {
		get {
			chk();
			return (bindGroup ??= DrawDeviceResources.For(device).CreateColorTexture2dViewBindGroup(texture.DefaultView)).AsRef();
		}
	}

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
	/// Height of mip level 0 in texels.
	/// </summary>
	public uint Height => texture.Height;

	/// <summary>
	/// Number of mip levels, including the base level.
	/// </summary>
	public uint MipLevelCount => texture.MipLevelCount;

	/// <summary>
	/// Texture format, which uploaded data has to be in. Always an sRGB format, i.e. either
	/// <see cref="TextureFormat.Rgba8UnormSrgb"/>, <see cref="TextureFormat.Bgra8UnormSrgb"/>, or
	/// one of the <c>*Srgb</c> block-compressed formats.
	/// </summary>
	public TextureFormat Format => texture.Format;

	/// <summary>
	/// Creates an empty <see cref="SrgbTexture2d"/>.
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
	/// Thrown if the format isn't an sRGB format or needs a feature the device doesn't have, or if
	/// the format is block-compressed and the size isn't a multiple of its block size.
	/// </exception>
	public SrgbTexture2d(GpuDevice device, in SrgbTexture2dParams @params) {
		ArgumentNullException.ThrowIfNull(device);
		ArgumentInvalidDefaultException.ThrowIfDefault(@params, nameof(@params));
		if (!@params.Format.IsSrgb())
			throw new ArgumentException($"format must be an sRGB format, got {@params.Format}", nameof(@params));
		uint mips = @params.Mips.Resolve(@params.Width, @params.Height, nameof(@params));
		this.device = device;
		texture = Texture2dCommon.Create(
			device,
			@params.Width,
			@params.Height,
			mips,
			@params.Format,
			TextureUsage.TextureBinding | TextureUsage.CopyDst
				| (mips > 1 && !@params.Format.IsBlockCompressed() ? TextureUsage.RenderAttachment : TextureUsage.None),
			viewFormat: null
		);
	}

	/// <summary>
	/// Uploads data to a sub-rectangle of the base mip level, and automatically generates the mip
	/// levels below from that, clobbering their existing contents. Doesn't support block-compressed
	/// formats; see <see cref="UploadToMip(uint, uint, uint, uint, uint, ReadOnlySpan{byte}, uint)"/>
	/// for those.
	/// </summary>
	/// <param name="x">Destination X offset in texels.</param>
	/// <param name="y">Destination Y offset in texels.</param>
	/// <param name="width">Width of the rectangle in texels.</param>
	/// <param name="height">Height of the rectangle in texels.</param>
	/// <param name="data">Texel data in the same format as <see cref="Format"/>.</param>
	/// <param name="bytesBetweenRows">
	/// Bytes from the start of one row in <paramref name="data"/> to the start of the next; the last
	/// row only takes its own texels' worth. See <see cref="GpuTextureLayout.BytesBetweenRows"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="width"/> or <paramref name="height"/> is 0.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the rectangle extends past the base level, <paramref name="bytesBetweenRows"/> is
	/// smaller than a tightly-packed row of <paramref name="width"/> texels, or
	/// <paramref name="data"/> is too small for the rectangle.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Format"/> is block-compressed, since mip generation for block-compressed
	/// formats isn't supported; thrown even if the texture only has a single mip level.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this texture has been disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Block-compressed formats aren't renderable, so mip generation for them is unsupported. This
	/// always throws for block-compressed formats, even if there's only a single mip level, so that 
	/// increasing the mip level count doesn't make this randomly start throwing. Use
	/// <see cref="UploadToMip(uint, uint, uint, uint, uint, ReadOnlySpan{byte}, uint)"/> instead for
	/// compressed formats, which works on single-mip textures too if you just upload to level 0. If
	/// you want to use a compressed format in combination with multiple mip levels, you kind of have
	/// to figure something out yourself to produce the mips.
	/// </para>
	/// <para>
	/// Every call regenerates the whole chain below the base level, so many small uploads cost a lot
	/// more than one big one. To do several small uploads in a batch, use
	/// <see cref="UploadToMip(uint, uint, uint, uint, uint, ReadOnlySpan{byte}, uint)"/> on level 0
	/// followed by <see cref="GenerateMips()"/>.
	/// </para>
	/// </remarks>
	public void Upload(uint x, uint y, uint width, uint height, ReadOnlySpan<byte> data, uint bytesBetweenRows) {
		chk();
		if (Format.IsBlockCompressed())
			throw new InvalidOperationException($"{Format} is block-compressed, so mip generation is unsupported for it; use UploadToMip instead");
		Texture2dCommon.Write(device, texture, 0, x, y, width, height, data, bytesBetweenRows);
		if (MipLevelCount > 1)
			SrgbTexture2dMipGenerator.For(device).Generate(texture);
	}

	/// <summary>
	/// Uploads data to the whole base mip level, and automatically generates the mip levels below
	/// from that, clobbering their existing contents. Doesn't support block-compressed formats; see
	/// <see cref="UploadToMip(uint, uint, uint, uint, uint, ReadOnlySpan{byte}, uint)"/> for those.
	/// </summary>
	/// <param name="data">Texel data in the same format as <see cref="Format"/>.</param>
	/// <param name="bytesBetweenRows">
	/// Bytes from the start of one row in <paramref name="data"/> to the start of the next; the last
	/// row only takes its own texels' worth. See <see cref="GpuTextureLayout.BytesBetweenRows"/>.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="bytesBetweenRows"/> is smaller than a tightly-packed row of
	/// <see cref="Width"/> texels, or <paramref name="data"/> is too small for the whole base level.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Format"/> is block-compressed, since mip generation for block-compressed
	/// formats isn't supported; thrown even if the texture only has a single mip level.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this texture has been disposed.
	/// </exception>
	/// <inheritdoc cref="Upload(uint, uint, uint, uint, ReadOnlySpan{byte}, uint)" path="/remarks"/>
	public void Upload(ReadOnlySpan<byte> data, uint bytesBetweenRows) => Upload(0, 0, Width, Height, data, bytesBetweenRows);

	/// <summary>
	/// Uploads data to a sub-rectangle of a mip level. Never touches other mip levels.
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
	/// Generates the mip levels below the base level from it, clobbering their existing contents.
	/// Doesn't support block-compressed formats. No-op for single-mip textures.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="Format"/> is block-compressed, since mip generation for block-compressed
	/// formats isn't supported; thrown even if the texture only has a single mip level.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this texture has been disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Block-compressed formats aren't renderable, so mip generation for them is unsupported. This
	/// always throws for block-compressed formats, even if there's only a single mip level, so that 
	/// increasing the mip level count doesn't make this randomly start throwing. If you want to use a
	/// compressed format in combination with multiple mip levels, you kind of have to figure something
	/// out yourself to produce the mips.
	/// </para>
	/// <para>
	/// For use after
	/// <see cref="UploadToMip(uint, uint, uint, uint, uint, ReadOnlySpan{byte}, uint)"/> on level 0,
	/// or after writing to <see cref="GpuTexture"/> directly. Does nothing for a single mip level, so
	/// a bunch of uploads to level 0 followed by one call of this works for any mip level count.
	/// </para>
	/// </remarks>
	public void GenerateMips() {
		chk();
		if (Format.IsBlockCompressed())
			throw new InvalidOperationException($"{Format} is block-compressed, so mip generation is unsupported for it");
		if (MipLevelCount > 1)
			SrgbTexture2dMipGenerator.For(device).Generate(texture);
	}

	/// <inheritdoc/>
	public bool IsAssetStoreOwned => custody is { IsReleased: false };

	void IAssetStoreValue.AttachCustody(AssetStoreCustody custody) => this.custody = custody;

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if an asset store owns this texture; see <see cref="IsAssetStoreOwned"/>.
	/// </exception>
	public void Dispose() {
		custody?.ThrowIfHeld(this);
		if (disposed)
			return;
		disposed = true;

		bindGroup?.Dispose();
		texture.Dispose();
	}

	private void chk() {
		if (!disposed)
			return;
		if (custody is not null)
			throw new ObjectDisposedException(nameof(SrgbTexture2d), $"this texture is {custody}, which the asset store reclaimed");
		throw new ObjectDisposedException(nameof(SrgbTexture2d));
	}
}

/// <summary>
/// Parameters used to create an <see cref="SrgbTexture2d"/>.
/// </summary>
/// <param name="Width">Texture width (i.e. width of mip level 0) in texels.</param>
/// <param name="Height">Texture height (i.e. height of mip level 0) in texels.</param>
/// <param name="Format">
/// Texture format; must be an sRGB format. Keep in mind that block-compressed formats need the
/// corresponding <see cref="GpuFeatures"/> on the device and a size that's a multiple of the
/// format's block size.
/// </param>
/// <param name="Mips">Mip level count.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its width and height are 0 and its format
/// is <see cref="TextureFormat.Undefined"/>.
/// </remarks>
public readonly record struct SrgbTexture2dParams(
	uint Width,
	uint Height,
	TextureFormat Format,
	MipLevels Mips = default
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
