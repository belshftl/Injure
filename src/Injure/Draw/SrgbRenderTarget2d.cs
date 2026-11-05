// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// Higher-level wrapper for a single-layer, single-mip, sampleable, and renderable 2D texture of
/// specifically an sRGB color format. Multisampling is currently unsupported, but is planned.
/// </summary>
/// <remarks>
/// <para>
/// "sRGB" refers to only the transfer function; see <see cref="TextureFormat"/>.
/// </para>
/// <para>
/// "sRGB color format" leaves only the RGBA8/BGRA8 unorm formats and the block-compressed formats,
/// and "renderable" additionally excludes block-compressed formats, so in practice the format can
/// only be either <see cref="TextureFormat.Rgba8UnormSrgb"/> or
/// <see cref="TextureFormat.Bgra8UnormSrgb"/>.
/// </para>
/// <para>
/// Since <see cref="Canvas"/> deals in premultiplied values, the target it draws into gets
/// premultiplied color, and all textures drawn there must be premultiplied. Therefore, the
/// contents of an <see cref="SrgbRenderTarget2d"/> are, in practice, always premultiplied; uploads
/// must supply correctly premultiplied data, and readback gives premultiplied values. (Note:
/// manual uploads or readback for <see cref="SrgbRenderTarget2d"/> are currently unimplemented.)
/// </para>
/// </remarks>
public sealed class SrgbRenderTarget2d : IDisposable {
	private readonly GpuDevice device;
	private readonly GpuTexture texture;
	private GpuBindGroup? bindGroup;
	private bool disposed = false;

	internal GpuBindGroupRef BindGroup {
		get {
			chk();
			return (bindGroup ??= DrawDeviceResources.For(device).CreateColorTexture2dViewBindGroup(texture.DefaultView)).AsRef();
		}
	}

	/// <summary>
	/// The underlying texture, guaranteed to have <see cref="TextureUsage.RenderAttachment"/> and
	/// <see cref="TextureUsage.TextureBinding"/> usages.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this render target has been disposed.
	/// </exception>
	public GpuTextureRef GpuTexture {
		get {
			chk();
			return texture.AsRef();
		}
	}

	/// <summary>
	/// Width of the render target in texels.
	/// </summary>
	public uint Width => texture.Width;

	/// <summary>
	/// Height of the render target in texels.
	/// </summary>
	public uint Height => texture.Height;

	/// <summary>
	/// Texture format. Always either <see cref="TextureFormat.Rgba8UnormSrgb"/> or
	/// <see cref="TextureFormat.Bgra8UnormSrgb"/>.
	/// </summary>
	public TextureFormat Format => texture.Format;

	/// <summary>
	/// Creates an empty <see cref="SrgbRenderTarget2d"/> with the given size and
	/// <see cref="TextureFormat.Rgba8UnormSrgb"/>.
	/// </summary>
	/// <param name="device">Device to create the render target on.</param>
	/// <param name="width">Width in texels.</param>
	/// <param name="height">Height in texels.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="width"/> or <paramref name="height"/> is 0.
	/// </exception>
	public SrgbRenderTarget2d(GpuDevice device, uint width, uint height) : this(
		device,
		new SrgbRenderTarget2dParams(width, height, TextureFormat.Rgba8UnormSrgb)
	) {
	}

	/// <summary>
	/// Creates an empty <see cref="SrgbRenderTarget2d"/>.
	/// </summary>
	/// <param name="device">Device to create the render target on.</param>
	/// <param name="params">Size and format.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if the width or height is 0.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the format isn't <see cref="TextureFormat.Rgba8UnormSrgb"/> or
	/// <see cref="TextureFormat.Bgra8UnormSrgb"/>.
	/// </exception>
	public SrgbRenderTarget2d(GpuDevice device, in SrgbRenderTarget2dParams @params) {
		this.device = device ?? throw new ArgumentNullException(nameof(device));
		ArgumentInvalidDefaultException.ThrowIfDefault(@params, nameof(@params));
		ArgumentOutOfRangeException.ThrowIfZero(@params.Width);
		ArgumentOutOfRangeException.ThrowIfZero(@params.Height);
		if (@params.Format.Tag is not TextureFormat.Case.Rgba8UnormSrgb and not TextureFormat.Case.Bgra8UnormSrgb)
			throw new ArgumentException($"SrgbRenderTarget2d format must be Rgba8UnormSrgb or Bgra8UnormSrgb, got {@params.Format}", nameof(@params));

		texture = Texture2dCommon.Create(
			device,
			@params.Width,
			@params.Height,
			mipLevelCount: 1,
			@params.Format,
			TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
			viewFormat: null
		);
	}

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		bindGroup?.Dispose();
		texture.Dispose();
	}

	private void chk() => ObjectDisposedException.ThrowIf(disposed, this);
}

/// <summary>
/// Parameters used to create a <see cref="SrgbRenderTarget2d"/>.
/// </summary>
/// <param name="Width">Render target width in texels.</param>
/// <param name="Height">Render target height in texels.</param>
/// <param name="Format">
/// Texture format; must be <see cref="TextureFormat.Rgba8UnormSrgb"/> or
/// <see cref="TextureFormat.Bgra8UnormSrgb"/>, since there aren't any other renderable sRGB
/// formats.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct SrgbRenderTarget2dParams(
	uint Width,
	uint Height,
	TextureFormat Format
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
