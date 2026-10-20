// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Common base type for texture wrappers, allowing APIs to accept both owning and non-owning
/// wrappers.
/// </summary>
public abstract class GpuTextureHandle {
	internal abstract WGPUTexture WgpuTexture { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUTexture"/>, bypassing ownership/lifetime. Dangles once
	/// freed by <see cref="GpuTexture.Dispose()"/>.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public WGPUTexture DangerousGetNative() => WgpuTexture;

	/// <summary>
	/// Checks if another <see cref="GpuTextureHandle"/> points to the same underlying WebGPU texture
	/// object as this one.
	/// </summary>
	/// <remarks>
	/// Not strict equality: if both this object and <paramref name="other"/> have been disposed and,
	/// as such, had the pointers to their underlying resources nulled, always returns false even
	/// though <c>null == null</c>. It follows that a <see cref="GpuTextureHandle"/> would be deemed
	/// not the same texture as itself if it's a disposed handle, <b>violating reflexivity.</b>
	/// </remarks>
	public bool SameTexture(GpuTextureHandle other) =>
		other.WgpuTexture.IsNotNull && WgpuTexture.Handle == other.WgpuTexture.Handle;

	/// <summary>
	/// Parameters the texture was created with. <see cref="GpuTextureParams.ViewFormats"/> is empty
	/// rather than <see langword="default"/> if the texture has no additional view formats.
	/// </summary>
	public abstract GpuTextureParams Params { get; }

	/// <summary>
	/// A view of the whole texture, with the texture's format and <see cref="DefaultViewDimension"/>.
	/// The texture owns it and disposes it along with itself.
	/// </summary>
	public abstract GpuTextureViewRef DefaultView { get; }

	/// <summary>
	/// Width of mip level 0 in texels.
	/// </summary>
	public uint Width => Params.Width;

	/// <summary>
	/// Height of mip level 0 in texels.
	/// </summary>
	public uint Height => Params.Height;

	/// <summary>
	/// Depth of mip level 0 in texels for 3D textures, or the number of array layers for 1D and 2D
	/// textures.
	/// </summary>
	public uint DepthOrArrayLayers => Params.DepthOrArrayLayers;

	/// <summary>
	/// Number of mip levels in the texture.
	/// </summary>
	public uint MipLevelCount => Params.MipLevelCount;

	/// <summary>
	/// Samples per texel; 1 unless the texture is multisampled.
	/// </summary>
	public uint SampleCount => Params.SampleCount;

	/// <summary>
	/// Texture dimension.
	/// </summary>
	public TextureDimension Dimension => Params.Dimension;

	/// <summary>
	/// Dimension used for the default view.
	/// </summary>
	/// <remarks>
	/// Matches the texture's dimension unless it's 2D and <see cref="DepthOrArrayLayers"/> is larger
	/// than 1, in which case this is <see cref="TextureViewDimension.Dimension2dArray"/>.
	/// </remarks>
	public abstract TextureViewDimension DefaultViewDimension { get; }

	/// <summary>
	/// Texture format.
	/// </summary>
	public TextureFormat Format => Params.Format;

	/// <summary>
	/// Allowed usages for this texture.
	/// </summary>
	public TextureUsage Usage => Params.Usage;

	/// <summary>
	/// Additional formats that can be used for created views on this texture.
	/// </summary>
	public ReadOnlySpan<TextureFormat> ViewFormats => Params.ViewFormats.AsSpan();

	/// <summary>
	/// Creates a view for this texture, returning an owning object.
	/// </summary>
	/// <param name="params">View creation parameters.</param>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <see cref="GpuTextureViewParams.Aspect"/> or a non-<see langword="null"/>
	/// <see cref="GpuTextureViewParams.Dimension"/> is the <see langword="default"/> value.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <see cref="GpuTextureViewParams.Format"/> is <see langword="null"/> and the
	/// texture's format has no default format for <see cref="GpuTextureViewParams.Aspect"/>.
	/// </exception>
	/// <remarks>
	/// The other requirements on <paramref name="params"/> are only validated by WebGPU.
	/// </remarks>
	public abstract GpuTextureView CreateView(in GpuTextureViewParams @params);

	// checks that region isn't a default value and names an existing mip level and lies within it
	internal void RequireRegionInBounds(in GpuTextureRegion region, string paramName) {
		ArgumentInvalidDefaultException.ThrowIfDefault(region, paramName);
		GpuArgChecks.Field(region.Aspect, paramName, nameof(region.Aspect));
		if (region.MipLevel >= MipLevelCount)
			throw new ArgumentException($"mip level {region.MipLevel} doesn't exist; the texture has {MipLevelCount}", paramName);
		uint w = Math.Max(1u, Width >> (int)region.MipLevel);
		uint h = Math.Max(1u, Height >> (int)region.MipLevel);
		uint d = Dimension == TextureDimension.Dimension3d ? Math.Max(1u, DepthOrArrayLayers >> (int)region.MipLevel) : DepthOrArrayLayers;
		if ((ulong)region.X + region.Width > w || (ulong)region.Y + region.Height > h || (ulong)region.Z + region.DepthOrArrayLayers > d)
			throw new ArgumentException($"region is out of the bounds of mip level {region.MipLevel} ({w}x{h}x{d})", paramName);
	}
}

/// <summary>
/// Owning wrapper around a GPU texture and its default view.
/// </summary>
public sealed unsafe class GpuTexture : GpuTextureHandle, IDisposable {
	private readonly GpuTextureView defaultView;
	private WGPUTexture tex;

	// `params` must already be resolved; see `Params`
	internal GpuTexture(WGPUTexture tex, in GpuTextureParams @params) {
		this.tex = tex;
		Params = @params;
		DefaultViewDimension = @params.Dimension.Tag switch {
			TextureDimension.Case.Dimension1d => TextureViewDimension.Dimension1d,
			TextureDimension.Case.Dimension2d =>
				@params.DepthOrArrayLayers > 1
					? TextureViewDimension.Dimension2dArray
					: TextureViewDimension.Dimension2d,
			TextureDimension.Case.Dimension3d => TextureViewDimension.Dimension3d,
			_ => throw new UnreachableException(),
		};
		defaultView = CreateView(new GpuTextureViewParams());
		DefaultView = defaultView.AsRef();
	}

	internal override WGPUTexture WgpuTexture => tex;
	/// <inheritdoc/>
	public override GpuTextureParams Params { get; }
	/// <inheritdoc/>
	public override GpuTextureViewRef DefaultView { get; }
	/// <inheritdoc/>
	public override TextureViewDimension DefaultViewDimension { get; }

	/// <inheritdoc/>
	public override GpuTextureView CreateView(in GpuTextureViewParams @params) {
		ArgumentInvalidDefaultException.ThrowIfDefault(@params, nameof(@params));
		GpuArgChecks.Field(@params.Aspect, nameof(@params), nameof(@params.Aspect));
		GpuArgChecks.Field(@params.Dimension, nameof(@params), nameof(@params.Dimension));
		TextureFormat fmt = @params.Format ?? (Format.Tag, @params.Aspect.Tag) switch {
			(TextureFormat.Case.Depth24PlusStencil8, TextureAspect.Case.DepthOnly) => TextureFormat.Depth24Plus,
			(TextureFormat.Case.Depth24PlusStencil8, TextureAspect.Case.StencilOnly) => TextureFormat.Stencil8,
			(TextureFormat.Case.Depth32FloatStencil8, TextureAspect.Case.DepthOnly) => TextureFormat.Depth32Float,
			(TextureFormat.Case.Depth32FloatStencil8, TextureAspect.Case.StencilOnly) => TextureFormat.Stencil8,
			(_, TextureAspect.Case.All) => Format,
			_ => throw new ArgumentException("texture format/aspect combination has no aspect-specific view format", nameof(@params)),
		};
		TextureViewDimension dim = @params.Dimension ?? DefaultViewDimension;
		uint mipLvCount = @params.MipLevelCount ?? checked(MipLevelCount - @params.BaseMipLevel);
		uint arrLayerCount = @params.ArrayLayerCount ?? dim.Tag switch {
			TextureViewDimension.Case.Dimension1d or TextureViewDimension.Case.Dimension2d or TextureViewDimension.Case.Dimension3d => 1,
			TextureViewDimension.Case.DimensionCube => 6,
			TextureViewDimension.Case.Dimension2dArray or TextureViewDimension.Case.DimensionCubeArray => DepthOrArrayLayers - @params.BaseArrayLayer,
			_ => throw new UnreachableException(),
		};
		WGPUTextureViewDescriptor desc = new() {
			format = fmt.ToWebgpuType(),
			dimension = dim.ToWebgpuType(),
			aspect = @params.Aspect.ToWebgpuType(),
			baseMipLevel = @params.BaseMipLevel,
			mipLevelCount = mipLvCount,
			baseArrayLayer = @params.BaseArrayLayer,
			arrayLayerCount = arrLayerCount,
		};
		return new GpuTextureView(
			WebgpuException.Check(wgpuTextureCreateView(WgpuTexture, &desc)),
			@params with {
				Format = fmt,
				Dimension = dim,
				MipLevelCount = mipLvCount,
				ArrayLayerCount = arrLayerCount,
			},
			Usage,
			Math.Max(1u, Width >> (int)@params.BaseMipLevel),
			dim == TextureViewDimension.Dimension1d ? 1u : Math.Max(1u, Height >> (int)@params.BaseMipLevel),
			dim != TextureViewDimension.Dimension3d ? 1u : Math.Max(1u, DepthOrArrayLayers >> (int)@params.BaseMipLevel),
			SampleCount
		);
	}

	/// <summary>
	/// Creates a non-owning view of this texture.
	/// </summary>
	public GpuTextureRef AsRef() => new(this);

	/// <summary>
	/// Disposes the default view and releases the underlying WebGPU texture.
	/// </summary>
	public void Dispose() {
		defaultView.Dispose();
		if (tex.IsNotNull)
			wgpuTextureRelease(tex);
		tex = default;
	}
}

/// <summary>
/// Non-owning wrapper around a GPU texture and its default view.
/// </summary>
public sealed class GpuTextureRef : GpuTextureHandle {
	private readonly GpuTexture source;
	internal GpuTextureRef(GpuTexture source) {
		this.source = source;
	}

	internal override WGPUTexture WgpuTexture => source.WgpuTexture;
	/// <inheritdoc/>
	public override GpuTextureParams Params => source.Params;
	/// <inheritdoc/>
	public override GpuTextureViewRef DefaultView => source.DefaultView;
	/// <inheritdoc/>
	public override TextureViewDimension DefaultViewDimension => source.DefaultViewDimension;
	/// <inheritdoc/>
	public override GpuTextureView CreateView(in GpuTextureViewParams @params) => source.CreateView(in @params);
}

/// <summary>
/// Parameters of a <see cref="GpuTexture"/>.
/// </summary>
/// <param name="Width">Width of mip level 0 in texels.</param>
/// <param name="Height">Height of mip level 0 in texels; 1 for 1D textures.</param>
/// <param name="DepthOrArrayLayers">
/// Depth of mip level 0 in texels for 3D textures, the number of array layers for 2D textures, and
/// 1 for 1D textures; see <see cref="TextureDimension"/>.
/// </param>
/// <param name="MipLevelCount">
/// Number of mip levels; at least 1. Each level is half the size of the previous one, rounding
/// towards negative infinity and stopping at 1, so the count can be at most
/// <c>floor(log2(size)) + 1</c> for the largest dimension that mip levels shrink.
/// </param>
/// <param name="SampleCount">
/// Samples per texel; 1 or 4. A multisampled texture has to be 2D with one mip level and one
/// array layer, and has to have <see cref="TextureUsage.RenderAttachment"/>.
/// </param>
/// <param name="Dimension">Texture dimension.</param>
/// <param name="Format">Texture format; never <see cref="TextureFormat.Undefined"/>.</param>
/// <param name="Usage">Allowed usages for this texture.</param>
/// <param name="ViewFormats">
/// Formats other than <paramref name="Format"/> that views of this texture can use, or
/// <see langword="default"/> for none. Each one has to differ from <paramref name="Format"/> only
/// in being sRGB-encoded or not; see <see cref="TextureFormatSrgbExtensions"/>. Must not contain
/// <paramref name="Format"/> or duplicates.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct GpuTextureParams(
	uint Width,
	uint Height,
	uint DepthOrArrayLayers,
	uint MipLevelCount,
	uint SampleCount,
	TextureDimension Dimension,
	TextureFormat Format,
	TextureUsage Usage,
	ImmutableArray<TextureFormat> ViewFormats = default
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// A region of texels within one mip level and aspect of a texture, for texture writes and copies.
/// </summary>
/// <param name="X">X offset of the region within the mip level, in texels.</param>
/// <param name="Y">Y offset of the region within the mip level, in texels.</param>
/// <param name="Z">
/// Z offset of the region within the mip level in texels for 3D textures, or the first array layer
/// for 2D textures.
/// </param>
/// <param name="Width">Width of the region in texels.</param>
/// <param name="Height">Height of the region in texels.</param>
/// <param name="DepthOrArrayLayers">
/// Depth of the region in texels for 3D textures, or the number of array layers for 2D textures.
/// </param>
/// <param name="MipLevel">
/// Mip level the region is in. Offsets and sizes are in that level's texels, and the region must
/// fit in its size.
/// </param>
/// <param name="Aspect">
/// Aspect to address. Regions of combined depth/stencil formats must be
/// <see cref="TextureAspect.DepthOnly"/> or <see cref="TextureAspect.StencilOnly"/>.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Aspect"/> is invalid.
/// </remarks>
public readonly record struct GpuTextureRegion(
	uint X,
	uint Y,
	uint Z,
	uint Width,
	uint Height,
	uint DepthOrArrayLayers,
	uint MipLevel,
	TextureAspect Aspect
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// Describes how texel data is laid out in memory or in a buffer, for texture writes and
/// buffer/texture copies.
/// </summary>
/// <param name="Offset">Byte offset to the start of the texel data.</param>
/// <param name="BytesPerRow">
/// <para>
/// Bytes between the starts of successive rows, or of successive rows of blocks for compressed
/// formats. Must be at least the size of one row of the region, and may be larger to skip padding
/// at the end of each row. <b>This does not prevent said padding from being read;</b> the entire
/// memory region used for a queue write must be initialized and readable.
/// </para>
/// <para>
/// Copies on a <see cref="GpuCommandEncoder"/> require this to be a multiple of 256; queue writes
/// don't. Only regions that are a single row can use a value of 0 for this.
/// </para>
/// </param>
/// <param name="RowsPerImage">
/// Rows between the starts of successive images, i.e. depth slices or array layers. Must be at
/// least the region's height. Only regions that are a single image can use a value of 0 for this.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct GpuTextureLayout(
	ulong Offset,
	uint BytesPerRow,
	uint RowsPerImage
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
