// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Injure.Assets;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

internal enum DrawableTextureKind {
	Texture2d,
	RenderTarget2d,
	Texture2dAsset,
}

/// <summary>
/// Something a <see cref="Canvas"/> can draw as a texture. Either a <see cref="Texture2d"/>, a
/// <see cref="RenderTarget2d"/>'s color texture, the current value of a <see cref="Versioned{T}"/>
/// of <see cref="Texture2d"/> (an <see cref="Asset{T}"/> or a <see cref="DerivedAsset{T}"/>), or a
/// <see cref="Texture2dSlice"/> of one of those.
/// </summary>
/// <remarks>
/// <para>
/// All four of the above implicitly cast to <see cref="DrawableTexture"/>. A
/// <see cref="Versioned{T}"/> is borrowed every time the texture is drawn, so a draw always uses
/// the current version.
/// </para>
/// <para>
/// Borrowing needs the drawing thread to be attached to the asset store, so drawing a
/// <see cref="Versioned{T}"/> from an unattached thread throws
/// <see cref="InvalidOperationException"/>. Either attach the thread with
/// <see cref="AssetStore.AttachCurrentThread()"/> (and report asset reload boundaries from it), or,
/// if it can't or shouldn't attach, manually pin it (<see cref="Versioned{T}.Pin()"/>) and draw the
/// pinned value.
/// </para>
/// <para>
/// Equality compares the referenced object, and the slice rectangle if there is one.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct DrawableTexture : IEquatable<DrawableTexture>, IHasInvalidDefault {
	private readonly object val;
	internal DrawableTextureKind Kind { get; }
	/// <remarks>
	/// In texels of the whole texture. <see langword="null"/> if this isn't a slice.
	/// </remarks>
	internal RectI? SliceRect { get; }

	private DrawableTexture(object val, DrawableTextureKind kind) {
		ArgumentNullException.ThrowIfNull(val);
		this.val = val;
		Kind = kind;
	}

	private DrawableTexture(in DrawableTexture whole, RectI sliceRect) {
		Debug.Assert(!whole.IsDefault && whole.SliceRect is null);
		val = whole.val;
		Kind = whole.Kind;
		SliceRect = sliceRect;
	}

	/// <summary>
	/// If this isn't a slice, just <c>this</c>; if this is a slice, the unsliced version.
	/// </summary>
	internal DrawableTexture Whole => SliceRect is null ? this : new DrawableTexture(val, Kind);

	/// <summary>
	/// The size of the whole texture, if it can be known without borrowing an asset.
	/// </summary>
	internal bool TryGetWholeSize(out uint width, out uint height) {
		switch (Kind) {
		case DrawableTextureKind.Texture2d:
			(width, height) = (((Texture2d)val).Width, ((Texture2d)val).Height);
			return true;
		case DrawableTextureKind.RenderTarget2d:
			(width, height) = (((RenderTarget2d)val).Width, ((RenderTarget2d)val).Height);
			return true;
		default:
			(width, height) = (0, 0);
			return false;
		}
	}

	/// <summary>
	/// Creates a source referring to <paramref name="tex"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator DrawableTexture(Texture2d tex) =>
		new(tex, DrawableTextureKind.Texture2d);

	/// <summary>
	/// Creates a source referring to <paramref name="rt"/>'s color texture.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="rt"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator DrawableTexture(RenderTarget2d rt) =>
		new(rt, DrawableTextureKind.RenderTarget2d);

	/// <summary>
	/// Creates a source referring to <paramref name="asset"/>, borrowed each time it's drawn.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="asset"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator DrawableTexture(Versioned<Texture2d> asset) =>
		new(asset, DrawableTextureKind.Texture2dAsset);

	/// <summary>
	/// Creates a source referring to the part of a texture that <paramref name="slice"/> covers.
	/// </summary>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="slice"/> is the <see langword="default"/> value.
	/// </exception>
	public static implicit operator DrawableTexture(Texture2dSlice slice) {
		if (slice.IsDefault)
			throw ArgumentInvalidDefaultException.For<Texture2dSlice>(nameof(slice));
		return new DrawableTexture(slice.Texture, slice.Rect);
	}

	public bool Equals(DrawableTexture other) => ReferenceEquals(val, other.val) && Kind == other.Kind && SliceRect == other.SliceRect;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is DrawableTexture other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(val), (int)Kind, SliceRect);
	public static bool operator ==(DrawableTexture left, DrawableTexture right) => left.Equals(right);
	public static bool operator !=(DrawableTexture left, DrawableTexture right) => !left.Equals(right);

	internal ResolvedDrawableTexture Resolve() => Kind switch {
		DrawableTextureKind.Texture2d => new ResolvedDrawableTexture((Texture2d)val),
		DrawableTextureKind.RenderTarget2d => new ResolvedDrawableTexture((RenderTarget2d)val),
		DrawableTextureKind.Texture2dAsset => new ResolvedDrawableTexture(((Versioned<Texture2d>)val).Borrow()),
		_ => throw new UnreachableException(),
	};

	internal bool IsDefault => val is null;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}

internal enum ResolvedDrawableTextureKind {
	Texture2d,
	RenderTarget2d,
	BorrowedTexture2d,
}

/// <summary>
/// A <see cref="DrawableTexture"/> resolved to something drawable, holding the borrow if it came
/// from an asset.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly ref struct ResolvedDrawableTexture : IHasInvalidDefault {
	private readonly Texture2d? texture = null;
	private readonly RenderTarget2d? renderTarget = null;
	// like the other payloads, only meaningful for its own kind
#pragma warning disable IJDEV0100
	private readonly AssetBorrow<Texture2d> borrow = default;
#pragma warning restore IJDEV0100

	public ResolvedDrawableTextureKind Kind { get; }

	public ResolvedDrawableTexture(Texture2d texture) {
		this.texture = texture;
		Kind = ResolvedDrawableTextureKind.Texture2d;
	}

	public ResolvedDrawableTexture(RenderTarget2d renderTarget) {
		this.renderTarget = renderTarget;
		Kind = ResolvedDrawableTextureKind.RenderTarget2d;
	}

	public ResolvedDrawableTexture(AssetBorrow<Texture2d> borrow) {
		this.borrow = borrow;
		Kind = ResolvedDrawableTextureKind.BorrowedTexture2d;
	}

	public uint Width => Kind switch {
		ResolvedDrawableTextureKind.Texture2d => texture!.Width,
		ResolvedDrawableTextureKind.RenderTarget2d => renderTarget!.Width,
		ResolvedDrawableTextureKind.BorrowedTexture2d => borrow.Value.Width,
		_ => throw new UnreachableException(),
	};
	public uint Height => Kind switch {
		ResolvedDrawableTextureKind.Texture2d => texture!.Height,
		ResolvedDrawableTextureKind.RenderTarget2d => renderTarget!.Height,
		ResolvedDrawableTextureKind.BorrowedTexture2d => borrow.Value.Height,
		_ => throw new UnreachableException(),
	};
	public GpuBindGroupRef BindGroup => Kind switch {
		ResolvedDrawableTextureKind.Texture2d => texture!.BindGroup,
		ResolvedDrawableTextureKind.RenderTarget2d => renderTarget!.ColorViewBindGroup,
		ResolvedDrawableTextureKind.BorrowedTexture2d => borrow.Value.BindGroup,
		_ => throw new UnreachableException(),
	};

	public object Identity => Kind switch {
		ResolvedDrawableTextureKind.Texture2d => texture!,
		ResolvedDrawableTextureKind.RenderTarget2d => renderTarget!,
		ResolvedDrawableTextureKind.BorrowedTexture2d => borrow.Value,
		_ => throw new UnreachableException(),
	};

	// comparing color textures only is enough since every render target has its own one
	public bool SameRenderTargetAs(RenderTarget2d rt) =>
		Kind == ResolvedDrawableTextureKind.RenderTarget2d && renderTarget!.ColorTexture.SameTexture(rt.ColorTexture);

	bool IHasInvalidDefault.IsDefault => Kind == ResolvedDrawableTextureKind.Texture2d && texture is null;
}
