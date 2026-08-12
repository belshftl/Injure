// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Injure.Rendering;

namespace Injure.Draw;

/// <summary>
/// High-level wrapper for an offscreen 2D render target.
/// </summary>
/// <remarks>
/// <para>
/// Owns a color texture, an optional depth/stencil texture, the color sampler,
/// and a lazy-created standard color texture bind group.
/// </para>
/// <para>
/// The color texture is always sampleable through <see cref="ColorView"/> and
/// <see cref="ColorBindGroup"/>. If a depth/stencil texture is present, its
/// attachment view is exposed through <see cref="DepthAttachmentView"/> and its
/// sampleable depth view is exposed through <see cref="DepthSampleView"/>.
/// </para>
/// </remarks>
public sealed class RenderTarget2d : IDisposable {
	private readonly WebGpuDevice device;
	private readonly GpuTexture colorTexture;
	private readonly GpuTexture? depthStencilTexture;
	private readonly GpuTextureView? depthSampleView; // only for depth+stencil formats
	private readonly GpuSampler colorSampler;
	private GpuBindGroup? colorBindGroup;
	private int disposed = 0;

	/// <summary>
	/// The color texture.
	/// </summary>
	public GpuTextureRef ColorTexture {
		get {
			chk();
			return colorTexture.AsRef();
		}
	}

	/// <summary>
	/// View for binding the color texture as a render attachment or sampling it.
	/// </summary>
	/// <remarks>
	/// This is the color texture's default view. Exists for convenience, and consistency
	/// with <see cref="DepthAttachmentView"/> / <see cref="DepthSampleView"/>.
	/// </remarks>
	public GpuTextureViewRef ColorView {
		get {
			chk();
			return colorTexture.DefaultView;
		}
	}

	/// <summary>
	/// The optional depth/stencil texture.
	/// </summary>
	public GpuTextureRef? DepthStencilTexture {
		get {
			chk();
			return depthStencilTexture?.AsRef();
		}
	}

	/// <summary>
	/// View for binding the depth/stencil texture as a render attachment.
	/// </summary>
	/// <remarks>
	/// For depth-only textures, this is the texture's default view.
	/// For depth+stencil textures, this is the view with both depth and stencil.
	/// </remarks>
	public GpuTextureViewRef? DepthAttachmentView {
		get {
			chk();
			return depthStencilTexture?.DefaultView;
		}
	}

	/// <summary>
	/// View for sampling the depth/stencil texture.
	/// </summary>
	/// <remarks>
	/// For depth-only textures, this is the texture's default view.
	/// For depth+stencil textures, this is a separate depth-only view.
	/// </remarks>
	public GpuTextureViewRef? DepthSampleView {
		get {
			chk();
			if (depthStencilTexture is null)
				return null;
			return depthSampleView?.AsRef() ?? depthStencilTexture.DefaultView;
		}
	}

	/// <summary>
	/// The color sampler, to be paired with <see cref="ColorView"/>.
	/// </summary>
	public GpuSampler ColorSampler {
		get {
			chk();
			return colorSampler;
		}
	}

	/// <summary>
	/// Lazy-created standard color texture bind group for <see cref="ColorView"/>
	/// and <see cref="ColorSampler"/>.
	/// </summary>
	public GpuBindGroupRef ColorBindGroup {
		get {
			chk();
			return (colorBindGroup ??= device.CreateStdColorTexture2dBindGroup(ColorView, ColorSampler)).AsRef();
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
	/// Depth/stencil attachment format, if present.
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
	/// Whether the render target has a depth/stencil attachment with stencil.
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
	/// Creates a new <see cref="RenderTarget2d"/> with the given size and <see cref="TextureFormat.RGBA8Unorm"/>.
	/// </summary>
	public RenderTarget2d(WebGpuDevice device, uint width, uint height) : this(device, new RenderTarget2dCreateParams(width, height, TextureFormat.RGBA8Unorm)) {
	}

	/// <summary>
	/// Creates a new <see cref="RenderTarget2d"/>.
	/// </summary>
	public RenderTarget2d(WebGpuDevice device, in RenderTarget2dCreateParams @params) {
		this.device = device ?? throw new ArgumentNullException(nameof(device));
		ArgumentOutOfRangeException.ThrowIfZero(@params.Width);
		ArgumentOutOfRangeException.ThrowIfZero(@params.Height);

		GpuTexture? color = null;
		GpuTexture? depthStencil = null;
		GpuTextureView? depthSample = null;
		GpuSampler? sampler = null;
		try {
			color = device.CreateTexture(
				new GpuTextureCreateParams(
					Width: @params.Width,
					Height: @params.Height,
					DepthOrArrayLayers: 1,
					MipLevelCount: 1,
					SampleCount: 1,
					Dimension: TextureDimension.Dimension2D,
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
						Dimension: TextureDimension.Dimension2D,
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
			sampler = device.CreateSampler(@params.ColorSampler ?? SamplerStates.NearestClamp);
			colorTexture = color;
			depthStencilTexture = depthStencil;
			depthSampleView = depthSample;
			colorSampler = sampler;
		} catch {
			sampler?.Dispose();
			depthSample?.Dispose();
			depthStencil?.Dispose();
			color?.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Creates a standard filtering depth texture bind group for <see cref="DepthSampleView"/>.
	/// </summary>
	/// <param name="sampler">Sampler to use.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this render target has no depth attachment.
	/// </exception>
	public GpuBindGroup CreateFilteringDepthBindGroup(GpuSamplerHandle sampler) {
		chk();
		GpuTextureViewRef view = DepthSampleView ?? throw new InvalidOperationException("render target has no depth attachment");
		return device.CreateStdFilteringDepthTexture2dBindGroup(view, sampler);
	}

	/// <summary>
	/// Creates a standard comparison depth texture bind group for <see cref="DepthSampleView"/>.
	/// </summary>
	/// <param name="sampler">Sampler to use.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this render target has no depth attachment.
	/// </exception>
	public GpuBindGroup CreateComparisonDepthBindGroup(GpuSamplerHandle sampler) {
		chk();
		GpuTextureViewRef view = DepthSampleView ?? throw new InvalidOperationException("render target has no depth attachment");
		return device.CreateStdComparisonDepthTexture2dBindGroup(view, sampler);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void chk() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool formatHasStencil(TextureFormat format) =>
		format.Tag is TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8 or TextureFormat.Case.Stencil8;

	/// <summary>
	/// Releases the owned GPU resources.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		colorBindGroup?.Dispose();
		colorSampler.Dispose();
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
/// <param name="ColorFormat">Color attachment format.</param>
/// <param name="DepthStencilFormat">
/// Depth/stencil attachment format, or <see langword="null"/> to
/// not include a depth/stencil attachment.
/// </param>
/// <param name="ColorSampler">
/// Sampler parameters used for the standard sampled color view.
/// If <see langword="null"/>, the default value of
/// <see cref="SamplerStates.NearestClamp"/> is used.
/// </param>
public readonly record struct RenderTarget2dCreateParams(
	uint Width,
	uint Height,
	TextureFormat ColorFormat,
	TextureFormat? DepthStencilFormat = null,
	GpuSamplerCreateParams? ColorSampler = null
);
