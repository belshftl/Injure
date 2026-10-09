// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Common base type for texture view wrappers, allowing APIs to accept both owning and non-owning
/// wrappers.
/// </summary>
public abstract class GpuTextureViewHandle {
	internal abstract WGPUTextureView WgpuTextureView { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUTextureView"/>, bypassing ownership/lifetime. Dangles
	/// once freed by <see cref="GpuTextureView.Dispose()"/>.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b> See
	/// See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public WGPUTextureView DangerousGetNative() => WgpuTextureView;

	/// <summary>
	/// Format of the view.
	/// </summary>
	public abstract TextureFormat Format { get; }

	/// <summary>
	/// Dimension of the view.
	/// </summary>
	public abstract TextureViewDimension Dimension { get; }

	/// <summary>
	/// Which aspects of the texture are accessible to the view.
	/// </summary>
	public abstract TextureAspect Aspect { get; }

	/// <summary>
	/// Allowed usages for this view.
	/// </summary>
	/// <remarks>
	/// Currently simply mirrors the texture's usages; proper support is planned.
	/// </remarks>
	public abstract TextureUsage Usage { get; }

	/// <summary>
	/// Base (most detailed) mipmap level accessible to the view.
	/// </summary>
	public abstract uint BaseMipLevel { get; }

	/// <summary>
	/// How many mipmap levels, starting with <see cref="BaseMipLevel"/>, are accessible to the view.
	/// </summary>
	public abstract uint MipLevelCount { get; }

	/// <summary>
	/// First array layer accessible to the view.
	/// </summary>
	public abstract uint BaseArrayLayer { get; }

	/// <summary>
	/// How many array layers, starting with <see cref="BaseArrayLayer"/>, are accessible to the view.
	/// </summary>
	public abstract uint ArrayLayerCount { get; }

	/// <summary>
	/// Width of the view.
	/// </summary>
	public abstract uint Width { get; }

	/// <summary>
	/// Height of the view (1 for 1D views).
	/// </summary>
	public abstract uint Height { get; }

	/// <summary>
	/// Depth of the view (1 for non-3D views).
	/// </summary>
	public abstract uint Depth { get; }

	/// <summary>
	/// Sample count of the view.
	/// </summary>
	public abstract uint SampleCount { get; }
}

/// <summary>
/// Owning wrapper around a texture view.
/// </summary>
public sealed class GpuTextureView : GpuTextureViewHandle, IDisposable {
	private WGPUTextureView texView;

	internal GpuTextureView(
		WGPUTextureView texView,
		TextureFormat format,
		TextureViewDimension dimension,
		TextureAspect aspect,
		TextureUsage usage,
		uint baseMipLevel,
		uint mipLevelCount,
		uint baseArrayLayer,
		uint arrayLayerCount,
		uint width,
		uint height,
		uint depth,
		uint sampleCount
	) {
		this.texView = texView;
		Format = format;
		Dimension = dimension;
		Aspect = aspect;
		Usage = usage;
		BaseMipLevel = baseMipLevel;
		MipLevelCount = mipLevelCount;
		BaseArrayLayer = baseArrayLayer;
		ArrayLayerCount = arrayLayerCount;
		Width = width;
		Height = height;
		Depth = depth;
		SampleCount = sampleCount;
	}

	internal override WGPUTextureView WgpuTextureView => texView;
	/// <inheritdoc/>
	public override TextureFormat Format { get; }
	/// <inheritdoc/>
	public override TextureViewDimension Dimension { get; }
	/// <inheritdoc/>
	public override TextureAspect Aspect { get; }
	/// <inheritdoc/>
	public override TextureUsage Usage { get; }
	/// <inheritdoc/>
	public override uint BaseMipLevel { get; }
	/// <inheritdoc/>
	public override uint MipLevelCount { get; }
	/// <inheritdoc/>
	public override uint BaseArrayLayer { get; }
	/// <inheritdoc/>
	public override uint ArrayLayerCount { get; }
	/// <inheritdoc/>
	public override uint Width { get; }
	/// <inheritdoc/>
	public override uint Height { get; }
	/// <inheritdoc/>
	public override uint Depth { get; }
	/// <inheritdoc/>
	public override uint SampleCount { get; }

	/// <summary>
	/// Creates a non-owning view of this texture view.
	/// </summary>
	public GpuTextureViewRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU texture view.
	/// </summary>
	public void Dispose() {
		if (texView.IsNotNull)
			wgpuTextureViewRelease(texView);
		texView = default;
	}
}

/// <summary>
/// Non-owning wrapper around a texture view.
/// </summary>
public sealed class GpuTextureViewRef : GpuTextureViewHandle {
	private readonly GpuTextureView source;
	internal GpuTextureViewRef(GpuTextureView source) {
		this.source = source;
	}

	internal override WGPUTextureView WgpuTextureView => source.WgpuTextureView;
	/// <inheritdoc/>
	public override TextureFormat Format => source.Format;
	/// <inheritdoc/>
	public override TextureViewDimension Dimension => source.Dimension;
	/// <inheritdoc/>
	public override TextureAspect Aspect => source.Aspect;
	/// <inheritdoc/>
	public override TextureUsage Usage => source.Usage;
	/// <inheritdoc/>
	public override uint BaseMipLevel => source.BaseMipLevel;
	/// <inheritdoc/>
	public override uint MipLevelCount => source.MipLevelCount;
	/// <inheritdoc/>
	public override uint BaseArrayLayer => source.BaseArrayLayer;
	/// <inheritdoc/>
	public override uint ArrayLayerCount => source.ArrayLayerCount;
	/// <inheritdoc/>
	public override uint Width => source.Width;
	/// <inheritdoc/>
	public override uint Height => source.Height;
	/// <inheritdoc/>
	public override uint Depth => source.Depth;
	/// <inheritdoc/>
	public override uint SampleCount => source.SampleCount;
}

/// <summary>
/// Parameters used to create a <see cref="GpuTextureView"/>.
/// </summary>
/// <param name="Format">
/// <para>
/// If <see cref="Aspect"/> is <see cref="TextureAspect.All"/>, this must be either the texture's
/// format or one of its <see cref="GpuTextureCreateParams.ViewFormats"/>. If <see cref="Aspect"/>
/// is <see cref="TextureAspect.DepthOnly"/> or <see cref="TextureAspect.StencilOnly"/>, this must
/// be the corresponding aspect-specific format of the texture's depth/stencil format.
/// </para>
/// <para>
/// Can be <see langword="null"/> to use the default of:
/// <list type="bullet">
/// <item><description>The texture's format for<see cref="TextureAspect.All"/>.</description></item>
/// <item><description>
/// <see cref="TextureFormat.Depth24Plus"/> or <see cref="TextureFormat.Depth32Float"/>
/// for depth-only views of combined depth+stencil textures.
/// </description></item>
/// <item><description>
/// <see cref="TextureFormat.Stencil8"/> for stencil-only views of combined depth+stencil textures.
/// </description></item>
/// </list>
/// Any other omitted-format combination is invalid and causes view creation to throw.
/// </para>
/// </param>
/// <param name="Dimension">
/// The dimension to view the texture as, or <see langword="null"/> to derive the dimension from the
/// texture.
/// </param>
/// <param name="Aspect">Which aspects of the texture are accessible to the view.</param>
/// <param name="BaseMipLevel">Base (most detailed) mipmap level accessible to the view.</param>
/// <param name="MipLevelCount">
/// How many mipmap levels, starting with <paramref name="BaseMipLevel"/>, are accessible to the
/// view, or <see langword="null"/> to use all remaining mip levels.
/// </param>
/// <param name="BaseArrayLayer">First array layer accessible to the view.</param>
/// <param name="ArrayLayerCount">
/// How many array layers, starting with <paramref name="BaseArrayLayer"/>, are accessible to the
/// view, or <see langword="null"/> to use the default of:
/// <list type="bullet">
/// <item><description><c>1</c> for <c>1d</c>, <c>2d</c>, or <c>3d</c> views.</description></item>
/// <item><description><c>6</c> for <c>cube</c> views.</description></item>
/// <item><description>
/// <c>texture.DepthOrArrayLayers - BaseArrayLayer</c> for <c>2d-array</c> or <c>cube-array</c>
/// views.
/// </description></item>
/// </list>
/// </param>
/// <remarks>
/// <para>
/// Usage flags are currently not settable; proper support is planned.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <see cref="Aspect"/> is
/// <see cref="TextureAspect.Undefined"/>. <c>new GpuTextureViewCreateParams()</c> views the whole
/// texture with the format and dimension derived from it.
/// </para>
/// </remarks>
public readonly record struct GpuTextureViewCreateParams(
	TextureFormat? Format,
	TextureViewDimension? Dimension,
	TextureAspect Aspect,
	uint BaseMipLevel = 0,
	uint? MipLevelCount = null,
	uint BaseArrayLayer = 0,
	uint? ArrayLayerCount = null
) {
	/// <summary>
	/// Creates parameters for a view of the whole texture, with the format and dimension derived
	/// from it.
	/// </summary>
	public GpuTextureViewCreateParams() : this(null, null, TextureAspect.All) {}
}
