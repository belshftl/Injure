// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for texture wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GpuTextureHandle {
	internal abstract WGPUTexture WgpuTexture { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUTexture"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUTexture DangerousGetNative() => WgpuTexture;

	/// <summary>
	/// Checks if another <see cref="GpuTextureHandle"/> points to the same
	/// underlying WebGPU texture object as this one.
	/// </summary>
	/// <remarks>
	/// If both this object and <paramref name="other"/> have been disposed and
	/// as such had the pointers to their underlying resources nulled out, always
	/// returns false even though <c>null == null</c> is technically true.
	/// </remarks>
	public bool SameTexture(GpuTextureHandle other) => other.WgpuTexture.IsNotNull && WgpuTexture.Handle == other.WgpuTexture.Handle;

	/// <summary>
	/// Gets the default texture view.
	/// </summary>
	public abstract GpuTextureViewRef DefaultView { get; }

	/// <summary>
	/// Width of the texture in texels.
	/// </summary>
	public abstract uint Width { get; }

	/// <summary>
	/// Height of the texture in texels.
	/// </summary>
	public abstract uint Height { get; }

	/// <summary>
	/// Depth of the texture in texels, or its array count.
	/// </summary>
	public abstract uint DepthOrArrayLayers { get; }

	/// <summary>
	/// Number of mip levels in the texture.
	/// </summary>
	public abstract uint MipLevelCount { get; }

	/// <summary>
	/// Sample count of the texture.
	/// </summary>
	public abstract uint SampleCount { get; }

	/// <summary>
	/// Texture dimension.
	/// </summary>
	public abstract TextureDimension Dimension { get; }

	/// <summary>
	/// Dimension used for the default view.
	/// </summary>
	/// <remarks>
	/// Matches the texture's dimension unless it's 2D and <see cref="DepthOrArrayLayers"/> is
	/// larger than 1, in which case this is <see cref="TextureViewDimension.Dimension2DArray"/>.
	/// </remarks>
	public abstract TextureViewDimension DefaultViewDimension { get; }

	/// <summary>
	/// Texture format.
	/// </summary>
	public abstract TextureFormat Format { get; }

	/// <summary>
	/// Allowed usages for this texture.
	/// </summary>
	public abstract TextureUsage Usage { get; }

	/// <summary>
	/// Additional formats that can be used for created views on this texture.
	/// </summary>
	public abstract ReadOnlySpan<TextureFormat> ViewFormats { get; }

	/// <summary>
	/// Creates a view for this texture, returning an owning object.
	/// </summary>
	/// <param name="params">View creation parameters.</param>
	public abstract GpuTextureView CreateView(in GpuTextureViewCreateParams @params);
}

/// <summary>
/// Owning wrapper around a GPU texture and its default view.
/// </summary>
public sealed unsafe class GpuTexture : GpuTextureHandle, IDisposable {
	private readonly GpuTextureView defaultView;
	private readonly TextureFormat[] viewFormats;
	private WGPUTexture tex;

	internal GpuTexture(
		WGPUTexture tex,
		uint width,
		uint height,
		uint depthOrArrayLayers,
		uint mipLevelCount,
		uint sampleCount,
		TextureDimension dimension,
		TextureFormat format,
		TextureUsage usage,
		TextureFormat[] viewFormats
	) {
		this.tex = tex;
		Width = width;
		Height = height;
		DepthOrArrayLayers = depthOrArrayLayers;
		MipLevelCount = mipLevelCount;
		SampleCount = sampleCount;
		Dimension = dimension;
		DefaultViewDimension = dimension.Tag switch {
			TextureDimension.Case.Dimension1D => TextureViewDimension.Dimension1D,
			TextureDimension.Case.Dimension2D => depthOrArrayLayers > 1 ? TextureViewDimension.Dimension2DArray : TextureViewDimension.Dimension2D,
			TextureDimension.Case.Dimension3D => TextureViewDimension.Dimension3D,
			_ => throw new UnreachableException(),
		};
		Format = format;
		Usage = usage;
		this.viewFormats = viewFormats; // trust the caller and don't copy out the array
		defaultView = CreateView(new GpuTextureViewCreateParams());
		DefaultView = defaultView.AsRef();
	}

	internal override WGPUTexture WgpuTexture => tex;
	public override GpuTextureViewRef DefaultView { get; }
	public override uint Width { get; }
	public override uint Height { get; }
	public override uint DepthOrArrayLayers { get; }
	public override uint MipLevelCount { get; }
	public override uint SampleCount { get; }
	public override TextureDimension Dimension { get; }
	public override TextureViewDimension DefaultViewDimension { get; }
	public override TextureFormat Format { get; }
	public override TextureUsage Usage { get; }
	public override ReadOnlySpan<TextureFormat> ViewFormats => viewFormats;

	public override GpuTextureView CreateView(in GpuTextureViewCreateParams @params) {
		TextureFormat fmt = @params.Format ?? (Format.Tag, @params.Aspect.Tag) switch {
			(TextureFormat.Case.Depth24PlusStencil8, TextureAspect.Case.DepthOnly) => TextureFormat.Depth24Plus,
			(TextureFormat.Case.Depth24PlusStencil8, TextureAspect.Case.StencilOnly) => TextureFormat.Stencil8,
			(TextureFormat.Case.Depth32FloatStencil8, TextureAspect.Case.DepthOnly) => TextureFormat.Depth32Float,
			(TextureFormat.Case.Depth32FloatStencil8, TextureAspect.Case.StencilOnly) => TextureFormat.Stencil8,
			(_, TextureAspect.Case.All) => Format,
			(_, TextureAspect.Case.Undefined) => throw new ArgumentException("texture aspect must not be Undefined", nameof(@params)),
			_ => throw new ArgumentException("texture format/aspect combination has no aspect-specific view format", nameof(@params)),
		};
		TextureViewDimension dim = @params.Dimension ?? DefaultViewDimension;
		uint mipLvCount = @params.MipLevelCount ?? checked(MipLevelCount - @params.BaseMipLevel);
		uint arrLayerCount = @params.ArrayLayerCount ?? dim.Tag switch {
			TextureViewDimension.Case.Dimension1D or TextureViewDimension.Case.Dimension2D or TextureViewDimension.Case.Dimension3D => 1,
			TextureViewDimension.Case.DimensionCube => 6,
			TextureViewDimension.Case.Dimension2DArray or TextureViewDimension.Case.DimensionCubeArray => DepthOrArrayLayers - @params.BaseArrayLayer,
			_ => throw new UnreachableException(),
		};
		WGPUTextureViewDescriptor desc = new() {
			format = fmt.ToWebGPUType(),
			dimension = dim.ToWebGPUType(),
			aspect = @params.Aspect.ToWebGPUType(),
			baseMipLevel = @params.BaseMipLevel,
			mipLevelCount = mipLvCount,
			baseArrayLayer = @params.BaseArrayLayer,
			arrayLayerCount = arrLayerCount,
		};
		return new GpuTextureView(
			WebGpuException.Check(wgpuTextureCreateView(WgpuTexture, &desc)),
			fmt,
			dim,
			@params.Aspect,
			Usage,
			@params.BaseMipLevel,
			mipLvCount,
			@params.BaseArrayLayer,
			arrLayerCount,
			Math.Max(1u, Width >> (int)@params.BaseMipLevel),
			dim == TextureViewDimension.Dimension1D ? 1u : Math.Max(1u, Height >> (int)@params.BaseMipLevel),
			dim != TextureViewDimension.Dimension3D ? 1u : Math.Max(1u, DepthOrArrayLayers >> (int)@params.BaseMipLevel),
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
	public override GpuTextureViewRef DefaultView => source.DefaultView;
	public override uint Width => source.Width;
	public override uint Height => source.Height;
	public override uint DepthOrArrayLayers => source.DepthOrArrayLayers;
	public override uint MipLevelCount => source.MipLevelCount;
	public override uint SampleCount => source.SampleCount;
	public override TextureDimension Dimension => source.Dimension;
	public override TextureViewDimension DefaultViewDimension => source.DefaultViewDimension;
	public override TextureFormat Format => source.Format;
	public override TextureUsage Usage => source.Usage;
	public override ReadOnlySpan<TextureFormat> ViewFormats => source.ViewFormats;
	public override GpuTextureView CreateView(in GpuTextureViewCreateParams @params) => source.CreateView(in @params);
}

/// <summary>
/// Parameters used to create a <see cref="GpuTexture"/>.
/// </summary>
/// <param name="Width">Texture width in texels.</param>
/// <param name="Height">Texture height in texels.</param>
/// <param name="DepthOrArrayLayers">Texture depth in texels or array layer count.</param>
/// <param name="MipLevelCount">Number of mip levels.</param>
/// <param name="SampleCount">Texture sample count.</param>
/// <param name="Dimension">Texture dimension.</param>
/// <param name="Format">Texture format.</param>
/// <param name="Usage">Allowed usages for this texture.</param>
/// <param name="ViewFormats">Additional formats that can be used for created views on this texture.</param>
public readonly record struct GpuTextureCreateParams(
	uint Width,
	uint Height,
	uint DepthOrArrayLayers,
	uint MipLevelCount,
	uint SampleCount,
	TextureDimension Dimension,
	TextureFormat Format,
	TextureUsage Usage,
	ImmutableArray<TextureFormat> ViewFormats = default
);

/// <summary>
/// Identifies a destination region and subresource within a texture.
/// </summary>
/// <param name="X">Destination X offset in texels.</param>
/// <param name="Y">Destination Y offset in texels.</param>
/// <param name="Z">Destination Z offset in texels or array layer base.</param>
/// <param name="Width">Width of the region in texels.</param>
/// <param name="Height">Height of the region in texels.</param>
/// <param name="DepthOrArrayLayers">Depth of the region in texels or array layer count.</param>
/// <param name="MipLevel">Destination mip level.</param>
/// <param name="Aspect">Texture aspect to address.</param>
public readonly record struct GpuTextureRegion(
	uint X,
	uint Y,
	uint Z,
	uint Width,
	uint Height,
	uint DepthOrArrayLayers,
	uint MipLevel,
	TextureAspect Aspect
);

/// <summary>
/// Describes the source memory layout for a texture upload.
/// </summary>
/// <param name="Offset">Byte offset to the start of the upload data.</param>
/// <param name="BytesPerRow">
/// Number of bytes between successive rows. May be larger than the
/// number of bytes to be actually read from each row, allowing row
/// padding/stride in the data.
/// </param>
/// <param name="RowsPerImage">Number of rows between successive images or array layers.</param>
public readonly record struct GpuTextureLayout(
	ulong Offset,
	uint BytesPerRow,
	uint RowsPerImage
);
