// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// High-level wrapper for an offscreen 2D render target.
/// </summary>
/// <remarks>
/// <para>
/// Owns a color texture and an optional depth/stencil texture, but no sampler; how
/// <see cref="Canvas"/> samples it is draw state (<see cref="TexSampling"/>).
/// </para>
/// <para>
/// The color texture is always sampleable through <see cref="ColorView"/>, or a bind group from
/// <see cref="CreateColorBindGroup(GpuSamplerHandle)"/>. If a depth/stencil texture is present, its
/// attachment view is exposed through <see cref="DepthAttachmentView"/> and its sampleable depth
/// view through <see cref="DepthSampleView"/>.
/// </para>
/// <para>
/// <see cref="Canvas"/> can draw into it (see <see cref="CanvasTarget"/>), which requires a
/// non-sRGB <see cref="ColorFormat"/>, and draw it (see <see cref="DrawableTexture"/>).
/// <see cref="Canvas"/> doesn't use the depth/stencil texture.
/// </para>
/// </remarks>
public sealed class RenderTarget2d : IDisposable {
	private readonly GpuDevice device;
	private readonly GpuTexture colorTexture;
	private readonly GpuTexture? depthStencilTexture;
	private readonly GpuTextureView? depthSampleView; // only for depth+stencil formats
	private GpuBindGroup? colorViewBindGroup;
	private int disposed = 0;

	/// <summary>
	/// The color texture.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this render target has been disposed.
	/// </exception>
	public GpuTextureRef ColorTexture {
		get {
			chk();
			return colorTexture.AsRef();
		}
	}

	/// <summary>
	/// View for binding the color texture as a render attachment or sampling it.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this render target has been disposed.
	/// </exception>
	/// <remarks>
	/// The color texture's default view; exists for convenience, and consistency with
	/// <see cref="DepthAttachmentView"/> and <see cref="DepthSampleView"/>.
	/// </remarks>
	public GpuTextureViewRef ColorView {
		get {
			chk();
			return colorTexture.DefaultView;
		}
	}

	/// <summary>
	/// The depth/stencil texture, or <see langword="null"/> if there's none.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this render target has been disposed.
	/// </exception>
	public GpuTextureRef? DepthStencilTexture {
		get {
			chk();
			return depthStencilTexture?.AsRef();
		}
	}

	/// <summary>
	/// View for binding the depth/stencil texture as a render attachment, or <see langword="null"/> if
	/// there's none.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this render target has been disposed.
	/// </exception>
	/// <remarks>
	/// This is said texture's default view, which covers depth and, if the format has it, stencil.
	/// </remarks>
	public GpuTextureViewRef? DepthAttachmentView {
		get {
			chk();
			return depthStencilTexture?.DefaultView;
		}
	}

	/// <summary>
	/// View for sampling the depth of the depth/stencil texture, or <see langword="null"/> if there's
	/// none.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this render target has been disposed.
	/// </exception>
	/// <remarks>
	/// For depth-only formats, said texture's default view; for formats with stencil, a separate
	/// depth-only view.
	/// </remarks>
	public GpuTextureViewRef? DepthSampleView {
		get {
			chk();
			if (depthStencilTexture is null)
				return null;
			return depthSampleView?.AsRef() ?? depthStencilTexture.DefaultView;
		}
	}

	// view-only, for Canvas, which binds the sampler separately
	internal GpuBindGroupRef ColorViewBindGroup {
		get {
			chk();
			return (colorViewBindGroup ??= DrawDeviceResources.For(device).CreateColorTexture2dViewBindGroup(ColorView)).AsRef();
		}
	}

	/// <summary>
	/// Width of the render target in texels.
	/// </summary>
	public uint Width => colorTexture.Width;

	/// <summary>
	/// Height of the render target in texels.
	/// </summary>
	public uint Height => colorTexture.Height;

	/// <summary>
	/// Color attachment format.
	/// </summary>
	public TextureFormat ColorFormat => colorTexture.Format;

	/// <summary>
	/// Depth/stencil attachment format, or <see langword="null"/> if there's no depth/stencil
	/// attachment.
	/// </summary>
	public TextureFormat? DepthStencilFormat => depthStencilTexture?.Format;

	/// <summary>
	/// Whether the render target has a depth/stencil attachment.
	/// </summary>
	[MemberNotNullWhen(
		true,
		nameof(DepthStencilTexture),
		nameof(DepthAttachmentView),
		nameof(DepthSampleView),
		nameof(DepthStencilFormat),
		nameof(depthStencilTexture)
	)]
	public bool HasDepth => depthStencilTexture is not null;

	/// <summary>
	/// Whether the render target has a depth/stencil attachment whose format has stencil.
	/// </summary>
	[MemberNotNullWhen(
		true,
		nameof(DepthStencilTexture),
		nameof(DepthAttachmentView),
		nameof(DepthSampleView),
		nameof(DepthStencilFormat),
		nameof(depthStencilTexture)
	)]
	public bool HasStencil => depthStencilTexture is not null && formatHasStencil(depthStencilTexture.Format);

	/// <summary>
	/// Creates a <see cref="RenderTarget2d"/> with the given size,
	/// <see cref="TextureFormat.Rgba8Unorm"/>, and no depth/stencil attachment.
	/// </summary>
	/// <param name="device">Device to create the render target on.</param>
	/// <param name="width">Width in texels.</param>
	/// <param name="height">Height in texels.</param>
	/// <inheritdoc cref="RenderTarget2d(GpuDevice, in RenderTarget2dCreateParams)" path="/exception"/>
	public RenderTarget2d(GpuDevice device, uint width, uint height) : this(device, new RenderTarget2dCreateParams(width, height, TextureFormat.Rgba8Unorm)) {
	}

	/// <summary>
	/// Creates a <see cref="RenderTarget2d"/>.
	/// </summary>
	/// <param name="device">Device to create the render target on.</param>
	/// <param name="params">Size and formats.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if the width or height is 0.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the color format is a depth/stencil format, or the depth/stencil format has no depth.
	/// </exception>
	public RenderTarget2d(GpuDevice device, in RenderTarget2dCreateParams @params) {
		this.device = device ?? throw new ArgumentNullException(nameof(device));
		ArgumentOutOfRangeException.ThrowIfZero(@params.Width);
		ArgumentOutOfRangeException.ThrowIfZero(@params.Height);
		if (formatHasDepth(@params.ColorFormat) || formatHasStencil(@params.ColorFormat))
			throw new ArgumentException($"color format must not be a depth/stencil format, got {@params.ColorFormat}", nameof(@params));
		if (@params.DepthStencilFormat is TextureFormat dsf && !formatHasDepth(dsf))
			throw new ArgumentException($"depth/stencil format must have depth, got {dsf}", nameof(@params));

		GpuTexture? color = null;
		GpuTexture? depthStencil = null;
		GpuTextureView? depthSample = null;
		try {
			color = device.CreateTexture(
				new GpuTextureCreateParams(
					Width: @params.Width,
					Height: @params.Height,
					DepthOrArrayLayers: 1,
					MipLevelCount: 1,
					SampleCount: 1,
					Dimension: TextureDimension.Dimension2d,
					Format: @params.ColorFormat,
					Usage: TextureUsage.RenderAttachment | TextureUsage.TextureBinding
				)
			);
			if (@params.DepthStencilFormat is TextureFormat fmt) {
				depthStencil = device.CreateTexture(
					new GpuTextureCreateParams(
						Width: @params.Width,
						Height: @params.Height,
						DepthOrArrayLayers: 1,
						MipLevelCount: 1,
						SampleCount: 1,
						Dimension: TextureDimension.Dimension2d,
						Format: fmt,
						Usage: TextureUsage.RenderAttachment | TextureUsage.TextureBinding
					)
				);
				// if the format has stencil we need a separate view for sampling
				depthSample = formatHasStencil(fmt)
					? depthStencil.CreateView(
						new GpuTextureViewCreateParams(
							Format: null,
							Dimension: null,
							Aspect: TextureAspect.DepthOnly
						)
					)
					: null;
			}
			colorTexture = color;
			depthStencilTexture = depthStencil;
			depthSampleView = depthSample;
		} catch {
			depthSample?.Dispose();
			depthStencil?.Dispose();
			color?.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Creates a bind group of <see cref="ColorView"/> and a filtering sampler, owned by the caller.
	/// </summary>
	/// <param name="sampler">Filtering sampler to pair with the view.</param>
	/// <remarks>
	/// The bind group has a <c>"float"</c>-sampled 2D texture at binding 0 and the sampler at binding
	/// 1. Whether <paramref name="sampler"/> is a filtering sampler isn't checked; if it isn't, WebGPU
	/// reports a validation error.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="sampler"/> is <see langword="null"/>.
	/// </exception>
	public GpuBindGroup CreateColorBindGroup(GpuSamplerHandle sampler) {
		ArgumentNullException.ThrowIfNull(sampler);
		chk();
		return DrawDeviceResources.For(device).CreateColorTexture2dBindGroup(ColorView, sampler);
	}

	/// <summary>
	/// Creates a bind group of <see cref="DepthSampleView"/> and a filtering sampler, owned by the
	/// caller.
	/// </summary>
	/// <param name="sampler">Filtering sampler to pair with the view.</param>
	/// <remarks>
	/// The bind group has a <c>"depth"</c>-sampled 2D texture at binding 0 and the sampler at binding
	/// 1. Whether <paramref name="sampler"/> is a filtering sampler isn't checked; if it isn't, WebGPU
	/// reports a validation error.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="sampler"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this render target has no depth attachment.
	/// </exception>
	public GpuBindGroup CreateFilteringDepthBindGroup(GpuSamplerHandle sampler) {
		ArgumentNullException.ThrowIfNull(sampler);
		chk();
		GpuTextureViewRef view = DepthSampleView ?? throw new InvalidOperationException("render target has no depth attachment");
		return DrawDeviceResources.For(device).CreateFilteringDepthTexture2dBindGroup(view, sampler);
	}

	/// <summary>
	/// Creates a bind group of <see cref="DepthSampleView"/> and a comparison sampler, owned by the
	/// caller.
	/// </summary>
	/// <param name="sampler">Comparison sampler to pair with the view.</param>
	/// <remarks>
	/// The bind group has a <c>"depth"</c>-sampled 2D texture at binding 0 and the sampler at binding
	/// 1. Whether <paramref name="sampler"/> is a comparison sampler isn't checked; if it isn't,
	/// WebGPU reports a validation error.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="sampler"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this render target has no depth attachment.
	/// </exception>
	public GpuBindGroup CreateComparisonDepthBindGroup(GpuSamplerHandle sampler) {
		ArgumentNullException.ThrowIfNull(sampler);
		chk();
		GpuTextureViewRef view = DepthSampleView ?? throw new InvalidOperationException("render target has no depth attachment");
		return DrawDeviceResources.For(device).CreateComparisonDepthTexture2dBindGroup(view, sampler);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void chk() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

	private static bool formatHasDepth(TextureFormat format) =>
		format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float
			or TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool formatHasStencil(TextureFormat format) =>
		format.Tag is TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8 or TextureFormat.Case.Stencil8;

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		colorViewBindGroup?.Dispose();
		depthSampleView?.Dispose();
		depthStencilTexture?.Dispose();
		colorTexture.Dispose();
	}
}

/// <summary>
/// Parameters used to create a <see cref="RenderTarget2d"/>.
/// </summary>
/// <param name="Width">Render target width in texels.</param>
/// <param name="Height">Render target height in texels.</param>
/// <param name="ColorFormat">
/// Color attachment format; not a depth/stencil format. Sampling it, e.g. drawing it with
/// <see cref="Canvas"/>, also needs a filterable <c>"float"</c> sample type, which isn't checked.
/// </param>
/// <param name="DepthStencilFormat">
/// Depth/stencil attachment format, which has to have depth (so not
/// <see cref="TextureFormat.Stencil8"/>), or <see langword="null"/> for no depth/stencil
/// attachment.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its width and height are 0 and its color
/// format is <see cref="TextureFormat.Undefined"/>.
/// </remarks>
public readonly record struct RenderTarget2dCreateParams(
	uint Width,
	uint Height,
	TextureFormat ColorFormat,
	TextureFormat? DepthStencilFormat = null
);
