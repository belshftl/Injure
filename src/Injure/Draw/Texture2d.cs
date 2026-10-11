// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Injure.Assets;
using Injure.Draw.PixelConv;
using Injure.DevAnalyzers.Attributes;
using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// Storage formats supported by <see cref="Texture2d"/>. The number is the total bits per texel.
/// </summary>
/// <remarks>
/// <para>
/// There are no sRGB formats, since <see cref="Canvas"/> takes texels as stored; color textures
/// hold sRGB-encoded values in plain unorm formats.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct Texture2dFormat {
	/// <summary>Raw switch tag for <see cref="Texture2dFormat"/>.</summary>
	public enum Case {
		/// <summary>
		/// One 8-bit unsigned normalized channel (R), e.g. for coverage masks.
		/// </summary>
		R8Unorm = 1,

		/// <summary>
		/// Two 8-bit unsigned normalized channels (RG).
		/// </summary>
		Rg16Unorm,

		/// <summary>
		/// Four 8-bit unsigned normalized channels (RGBA).
		/// </summary>
		Rgba32Unorm,

		/// <summary>
		/// Four 8-bit unsigned normalized channels, stored in BGRA order.
		/// </summary>
		Bgra32Unorm,
	}
}

/// <summary>
/// High-level wrapper for a 2D color texture.
/// </summary>
/// <remarks>
/// <para>
/// Intentionally narrower than <see cref="GpuTexture"/>; always represents a single-layer,
/// non-multisampled, sampleable 2D color or mask texture with a default view and a sampler, which
/// <see cref="Canvas"/> uses when drawing it. Currently single-mip too, but that decision is not
/// final.
/// </para>
/// <para>
/// If this <see cref="Texture2d"/> was obtained by borrowing an <see cref="AssetRef{T}"/>, it's
/// revoked once that lease expires, to make misuse more difficult. After revocation, members that
/// use the texture throw <see cref="AssetLeaseExpiredException"/>; after disposal, they throw
/// <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public sealed class Texture2d : IRevokable, IDisposable {
	private readonly GpuDevice device;
	private readonly GpuTexture texture;
	private readonly GpuSampler sampler;
	private GpuBindGroup? bindGroup = null;
	private int disposed = 0;
	private int revoked = 0;

	internal GpuTexture Texture {
		get {
			chk();
			return texture;
		}
	}
	internal GpuSampler Sampler {
		get {
			chk();
			return sampler;
		}
	}
	internal GpuBindGroupRef BindGroup {
		get {
			chk();
			return (bindGroup ??= DrawDeviceResources.For(device).CreateColorTexture2dBindGroup(Texture.DefaultView, Sampler)).AsRef();
		}
	}

	/// <summary>
	/// Returns the underlying <see cref="GpuTexture"/>, bypassing revocation. Keeps working after this
	/// texture is revoked, until it's disposed by <see cref="Dispose()"/>.
	/// </summary>
	/// <remarks>
	/// See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public GpuTextureRef DangerousGetGpuTexture() => Texture.AsRef();

	/// <summary>
	/// Returns the underlying <see cref="GpuSampler"/>, bypassing revocation. Keeps working after this
	/// texture is revoked, until it's disposed by <see cref="Dispose()"/>.
	/// </summary>
	/// <remarks>
	/// See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public GpuSamplerRef DangerousGetGpuSampler() => Sampler.AsRef();

	/// <summary>
	/// Returns the bind group of the texture's default view and sampler that <see cref="Canvas"/>
	/// uses, bypassing revocation. Keeps working after this texture is revoked, until it's disposed by
	/// <see cref="Dispose()"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bind group has a <c>"float"</c>-sampled 2D texture at binding 0 and a filtering sampler at
	/// binding 1.
	/// </para>
	/// <para>
	/// See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </para>
	/// </remarks>
	public GpuBindGroupRef DangerousGetBindGroup() => BindGroup;

	/// <summary>
	/// Width of the texture in texels.
	/// </summary>
	public uint Width => Texture.Width;

	/// <summary>
	/// Height of the texture in texels.
	/// </summary>
	public uint Height => Texture.Height;

	/// <summary>
	/// High-level storage format of the texture.
	/// </summary>
	public Texture2dFormat Format { get; }

	/// <summary>
	/// The pixel format that
	/// <see cref="Upload(ReadOnlySpan{byte}, int, PixelFormat, PixelConvertOptions)"/> converts
	/// uploaded data to.
	/// </summary>
	public PixelFormat UploadPixelFormat { get; }

	/// <summary>
	/// Creates an empty <see cref="Texture2d"/> with the given size,
	/// <see cref="Texture2dFormat.Rgba32Unorm"/>, and <see cref="SamplerStates.NearestClamp"/>.
	/// </summary>
	/// <param name="device">Device to create the texture on.</param>
	/// <param name="width">Width in texels.</param>
	/// <param name="height">Height in texels.</param>
	/// <inheritdoc cref="Texture2d(GpuDevice, in Texture2dCreateParams)" path="/exception"/>
	public Texture2d(GpuDevice device, uint width, uint height)
		: this(device, new Texture2dCreateParams(width, height)) {
	}

	/// <summary>
	/// Creates an empty <see cref="Texture2d"/> with the given size and format, and
	/// <see cref="SamplerStates.NearestClamp"/>.
	/// </summary>
	/// <param name="device">Device to create the texture on.</param>
	/// <param name="width">Width in texels.</param>
	/// <param name="height">Height in texels.</param>
	/// <param name="format">Storage format.</param>
	/// <inheritdoc cref="Texture2d(GpuDevice, in Texture2dCreateParams)" path="/exception"/>
	public Texture2d(GpuDevice device, uint width, uint height, Texture2dFormat format)
		: this(device, new Texture2dCreateParams(width, height, format)) {
	}

	/// <summary>
	/// Creates an empty <see cref="Texture2d"/>.
	/// </summary>
	/// <param name="device">Device to create the texture on.</param>
	/// <param name="params">Size, format, and sampler.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if the width or height is 0.
	/// </exception>
	public Texture2d(GpuDevice device, in Texture2dCreateParams @params) {
		ArgumentNullException.ThrowIfNull(device);
		ArgumentOutOfRangeException.ThrowIfZero(@params.Width);
		ArgumentOutOfRangeException.ThrowIfZero(@params.Height);

		this.device = device;
		Texture2dFormat fmt = @params.Format ?? Texture2dFormat.Rgba32Unorm;
		Format = fmt;
		UploadPixelFormat = getUploadPixelFormat(fmt);

		GpuTexture? texture = null;
		GpuSampler? sampler = null;
		try {
			texture = device.CreateTexture(
				new GpuTextureCreateParams(
					Width: @params.Width,
					Height: @params.Height,
					DepthOrArrayLayers: 1,
					MipLevelCount: 1,
					SampleCount: 1,
					Dimension: TextureDimension.Dimension2d,
					Format: getTextureFormat(fmt),
					Usage: TextureUsage.TextureBinding | TextureUsage.CopyDst
				)
			);
			sampler = device.CreateSampler(@params.SamplerParams ?? SamplerStates.NearestClamp);
			this.texture = texture;
			this.sampler = sampler;
			texture = null;
			sampler = null;
		} finally {
			sampler?.Dispose();
			texture?.Dispose();
		}
	}

	/// <summary>
	/// Uploads pixel data to the entire texture.
	/// </summary>
	/// <param name="src">Source pixel data.</param>
	/// <param name="srcStride">Stride of <paramref name="src"/> in bytes (not pixels).</param>
	/// <param name="srcFmt">Pixel format of <paramref name="src"/>.</param>
	/// <param name="opts">Pixel conversion options.</param>
	/// <inheritdoc cref="Upload(int, int, ReadOnlySpan{byte}, int, PixelFormat, int, int, PixelConvertOptions)" path="/exception"/>
	public void Upload(ReadOnlySpan<byte> src, int srcStride, PixelFormat srcFmt, PixelConvertOptions opts = default) =>
		Upload(0, 0, src, srcStride, srcFmt, checked((int)Width), checked((int)Height), opts);

	/// <summary>
	/// Uploads pixel data to a sub-rectangle of the texture.
	/// </summary>
	/// <param name="x">Destination X offset in texels.</param>
	/// <param name="y">Destination Y offset in texels.</param>
	/// <param name="src">Source pixel data.</param>
	/// <param name="srcStride">Stride of <paramref name="src"/> in bytes (not pixels).</param>
	/// <param name="srcFmt">Pixel format of <paramref name="src"/>.</param>
	/// <param name="width">Width of the upload rectangle in texels.</param>
	/// <param name="height">Height of the upload rectangle in texels.</param>
	/// <param name="opts">Pixel conversion options.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="x"/> or <paramref name="y"/> is negative, or
	/// <paramref name="srcStride"/>, <paramref name="width"/>, or <paramref name="height"/> is
	/// negative or zero.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the rectangle extends past the texture, if <paramref name="srcStride"/> is smaller
	/// than a tightly packed row of <paramref name="width"/> pixels, or if <paramref name="src"/> is
	/// too small for the rectangle.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if converting from <paramref name="srcFmt"/> would narrow, drop alpha, or drop color
	/// channels without the corresponding <see cref="ConversionFlags"/> in <paramref name="opts"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this texture has been disposed.
	/// </exception>
	/// <exception cref="AssetLeaseExpiredException">
	/// Thrown if this texture has been revoked.
	/// </exception>
	public void Upload(int x, int y, ReadOnlySpan<byte> src, int srcStride, PixelFormat srcFmt, int width, int height, PixelConvertOptions opts = default) {
		chk();
		ArgumentOutOfRangeException.ThrowIfNegative(x);
		ArgumentOutOfRangeException.ThrowIfNegative(y);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(srcStride);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		if ((uint)x + (uint)width > Width || (uint)y + (uint)height > Height)
			throw new ArgumentException("upload rectangle is out of bounds of the texture");

		byte[] converted = PixelConverter.Convert(src, srcStride, srcFmt, UploadPixelFormat, out int dstStride, width, height, opts);
		device.WriteToTexture(
			texture,
			new GpuTextureRegion(
				X: (uint)x,
				Y: (uint)y,
				Z: 0,
				Width: (uint)width,
				Height: (uint)height,
				DepthOrArrayLayers: 1,
				MipLevel: 0,
				Aspect: TextureAspect.All
			),
			converted,
			new GpuTextureLayout(
				Offset: 0,
				BytesPerRow: (uint)dstStride,
				RowsPerImage: (uint)height
			)
		);
	}

	/// <summary>
	/// Revokes this texture, invalidating further use.
	/// </summary>
	/// <remarks>
	/// Used by the asset system when the lease of a <see cref="Texture2d"/> borrowed from an
	/// <see cref="AssetRef{T}"/> expires. Revocation is logical invalidation only; the GPU resources
	/// stay alive until <see cref="Dispose()"/>.
	/// </remarks>
	public void Revoke() => Volatile.Write(ref revoked, 1);

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;

		bindGroup?.Dispose();
		sampler.Dispose();
		texture.Dispose();
	}

	private static TextureFormat getTextureFormat(Texture2dFormat fmt) => fmt.Tag switch {
		Texture2dFormat.Case.R8Unorm => TextureFormat.R8Unorm,
		Texture2dFormat.Case.Rg16Unorm => TextureFormat.Rg8Unorm,
		Texture2dFormat.Case.Rgba32Unorm => TextureFormat.Rgba8Unorm,
		Texture2dFormat.Case.Bgra32Unorm => TextureFormat.Bgra8Unorm,
		_ => throw new UnreachableException(),
	};

	private static PixelFormat getUploadPixelFormat(Texture2dFormat fmt) => fmt.Tag switch {
		Texture2dFormat.Case.R8Unorm => PixelFormat.R8_Unorm,
		Texture2dFormat.Case.Rg16Unorm => PixelFormat.Rg16_Unorm,
		Texture2dFormat.Case.Rgba32Unorm => PixelFormat.Rgba32_Unorm,
		Texture2dFormat.Case.Bgra32Unorm => PixelFormat.Bgra32_Unorm,
		_ => throw new UnreachableException(),
	};

	private void chk() {
		// check revoked first for more helpful use-after-lease-expiry exceptions
		if (Volatile.Read(ref revoked) != 0)
			throw new AssetLeaseExpiredException("borrowed texture was used after its lease expired");
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
	}
}

/// <summary>
/// Parameters used to create a <see cref="Texture2d"/>.
/// </summary>
/// <param name="Width">Texture width in texels.</param>
/// <param name="Height">Texture height in texels.</param>
/// <param name="Format">
/// Storage format, or <see langword="null"/> for <see cref="Texture2dFormat.Rgba32Unorm"/>.
/// </param>
/// <param name="SamplerParams">
/// Parameters of the sampler created alongside the texture, which <see cref="Canvas"/> draws it
/// with, or <see langword="null"/> for <see cref="SamplerStates.NearestClamp"/>.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its width and height are 0 and its
/// <see cref="Format"/> is invalid.
/// </remarks>
public readonly record struct Texture2dCreateParams(
	uint Width,
	uint Height,
	Texture2dFormat? Format = null,
	GpuSamplerCreateParams? SamplerParams = null
);
