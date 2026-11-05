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
	SrgbTexture2d,
	SrgbRenderTarget2d,
	SrgbTexture2dAsset,
}

/// <summary>
/// Something <see cref="Canvas.Texture(DrawableTexture, in TexDraw)"/> can draw. Either a
/// <see cref="SrgbTexture2d"/>, a <see cref="SrgbRenderTarget2d"/>'s color texture, the current
/// value of a <see cref="Versioned{T}"/> of <see cref="SrgbTexture2d"/> (an
/// <see cref="Asset{T}"/> or a <see cref="DerivedAsset{T}"/>), or a
/// <see cref="DrawableTextureSlice"/> of one of those.
/// </summary>
/// <remarks>
/// <para>
/// All four of the above implicitly cast to <see cref="DrawableTexture"/>;
/// <see cref="RawTexture2d"/> doesn't, since a raw-texture material is required to draw one. A
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
		case DrawableTextureKind.SrgbTexture2d:
			(width, height) = (((SrgbTexture2d)val).Width, ((SrgbTexture2d)val).Height);
			return true;
		case DrawableTextureKind.SrgbRenderTarget2d:
			(width, height) = (((SrgbRenderTarget2d)val).Width, ((SrgbRenderTarget2d)val).Height);
			return true;
		default:
			(width, height) = (0, 0);
			return false;
		}
	}

	/// <summary>
	/// Creates a <see cref="DrawableTexture"/> from <paramref name="tex"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator DrawableTexture(SrgbTexture2d tex) =>
		new(tex, DrawableTextureKind.SrgbTexture2d);

	/// <summary>
	/// Creates a <see cref="DrawableTexture"/> from <paramref name="rt"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="rt"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator DrawableTexture(SrgbRenderTarget2d rt) =>
		new(rt, DrawableTextureKind.SrgbRenderTarget2d);

	/// <summary>
	/// Creates a <see cref="DrawableTexture"/> from <paramref name="asset"/>, freshly borrowing the
	/// asset on every draw.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="asset"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator DrawableTexture(Versioned<SrgbTexture2d> asset) =>
		new(asset, DrawableTextureKind.SrgbTexture2dAsset);

	/// <summary>
	/// Creates a <see cref="DrawableTexture"/> from <paramref name="slice"/>.
	/// </summary>
	public static implicit operator DrawableTexture(DrawableTextureSlice slice) {
		ArgumentInvalidDefaultException.ThrowIfDefault(slice);
		return new DrawableTexture(slice.Texture, slice.Rect);
	}

	public bool Equals(DrawableTexture other) => ReferenceEquals(val, other.val) && Kind == other.Kind && SliceRect == other.SliceRect;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is DrawableTexture other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(val), (int)Kind, SliceRect);
	public static bool operator ==(DrawableTexture left, DrawableTexture right) => left.Equals(right);
	public static bool operator !=(DrawableTexture left, DrawableTexture right) => !left.Equals(right);

	internal ResolvedDrawableTexture Resolve() => Kind switch {
		DrawableTextureKind.SrgbTexture2d => new ResolvedDrawableTexture((SrgbTexture2d)val),
		DrawableTextureKind.SrgbRenderTarget2d => new ResolvedDrawableTexture((SrgbRenderTarget2d)val),
		DrawableTextureKind.SrgbTexture2dAsset => new ResolvedDrawableTexture(((Versioned<SrgbTexture2d>)val).Borrow()),
		_ => throw new UnreachableException(),
	};

	internal bool IsDefault => val is null;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}

internal enum ResolvedDrawableTextureKind {
	SrgbTexture2d,
	SrgbRenderTarget2d,
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
	private readonly SrgbTexture2d? texture = null;
	private readonly SrgbRenderTarget2d? renderTarget = null;
	// like the other payloads, only meaningful for its own kind
#pragma warning disable IJDEV0100
	private readonly AssetBorrow<SrgbTexture2d> borrow = default;
#pragma warning restore IJDEV0100

	public ResolvedDrawableTextureKind Kind { get; }

	public ResolvedDrawableTexture(SrgbTexture2d texture) {
		this.texture = texture;
		Kind = ResolvedDrawableTextureKind.SrgbTexture2d;
	}

	public ResolvedDrawableTexture(SrgbRenderTarget2d renderTarget) {
		this.renderTarget = renderTarget;
		Kind = ResolvedDrawableTextureKind.SrgbRenderTarget2d;
	}

	public ResolvedDrawableTexture(AssetBorrow<SrgbTexture2d> borrow) {
		this.borrow = borrow;
		Kind = ResolvedDrawableTextureKind.BorrowedTexture2d;
	}

	public uint Width => Kind switch {
		ResolvedDrawableTextureKind.SrgbTexture2d => texture!.Width,
		ResolvedDrawableTextureKind.SrgbRenderTarget2d => renderTarget!.Width,
		ResolvedDrawableTextureKind.BorrowedTexture2d => borrow.Value.Width,
		_ => throw new UnreachableException(),
	};
	public uint Height => Kind switch {
		ResolvedDrawableTextureKind.SrgbTexture2d => texture!.Height,
		ResolvedDrawableTextureKind.SrgbRenderTarget2d => renderTarget!.Height,
		ResolvedDrawableTextureKind.BorrowedTexture2d => borrow.Value.Height,
		_ => throw new UnreachableException(),
	};
	public GpuBindGroupRef BindGroup => Kind switch {
		ResolvedDrawableTextureKind.SrgbTexture2d => texture!.BindGroup,
		ResolvedDrawableTextureKind.SrgbRenderTarget2d => renderTarget!.BindGroup,
		ResolvedDrawableTextureKind.BorrowedTexture2d => borrow.Value.BindGroup,
		_ => throw new UnreachableException(),
	};

	public object Identity => Kind switch {
		ResolvedDrawableTextureKind.SrgbTexture2d => texture!,
		ResolvedDrawableTextureKind.SrgbRenderTarget2d => renderTarget!,
		ResolvedDrawableTextureKind.BorrowedTexture2d => borrow.Value,
		_ => throw new UnreachableException(),
	};

	public bool SameRenderTargetAs(SrgbRenderTarget2d rt) =>
		Kind == ResolvedDrawableTextureKind.SrgbRenderTarget2d && renderTarget!.GpuTexture.SameTexture(rt.GpuTexture);

	bool IHasInvalidDefault.IsDefault => Kind == ResolvedDrawableTextureKind.SrgbTexture2d && texture is null;
}
