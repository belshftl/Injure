// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Injure.Assets;
using Injure.Rendering;

namespace Injure.Draw;

internal enum TextureSourceKind {
	Texture2d,
	RenderTarget2d,
	Texture2dAssetRef,
}

public readonly struct TextureSource : IEquatable<TextureSource> {
	private readonly object val;
	internal TextureSourceKind Kind { get; }

	private TextureSource(object val, TextureSourceKind kind) {
		ArgumentNullException.ThrowIfNull(val);
		this.val = val;
		Kind = kind;
	}

	public static implicit operator TextureSource(Texture2d tex) =>
		new(tex, TextureSourceKind.Texture2d);
	public static implicit operator TextureSource(RenderTarget2d rt) =>
		new(rt, TextureSourceKind.RenderTarget2d);
	public static implicit operator TextureSource(AssetRef<Texture2d> asset) =>
		new(asset, TextureSourceKind.Texture2dAssetRef);

	public bool Equals(TextureSource other) => ReferenceEquals(val, other.val) && Kind == other.Kind;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextureSource other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(val), (int)Kind);
	public static bool operator ==(TextureSource left, TextureSource right) => left.Equals(right);
	public static bool operator !=(TextureSource left, TextureSource right) => !left.Equals(right);

	internal ResolvedTextureSource Resolve() => Kind switch {
		TextureSourceKind.Texture2d => new ResolvedTextureSource((Texture2d)val),
		TextureSourceKind.RenderTarget2d => new ResolvedTextureSource((RenderTarget2d)val),
		TextureSourceKind.Texture2dAssetRef => new ResolvedTextureSource(((AssetRef<Texture2d>)val).Borrow()),
		_ => throw new UnreachableException(),
	};
}

internal enum ResolvedTextureSourceKind {
	Texture2d,
	RenderTarget2d,
	LeasedTexture2d,
}

internal readonly ref struct ResolvedTextureSource {
	private readonly Texture2d? texture = null;
	private readonly RenderTarget2d? renderTarget = null;
	private readonly AssetLease<Texture2d> lease = default;

	public ResolvedTextureSourceKind Kind { get; }

	public ResolvedTextureSource(Texture2d texture) {
		this.texture = texture;
		Kind = ResolvedTextureSourceKind.Texture2d;
	}

	public ResolvedTextureSource(RenderTarget2d renderTarget) {
		this.renderTarget = renderTarget;
		Kind = ResolvedTextureSourceKind.RenderTarget2d;
	}

	public ResolvedTextureSource(AssetLease<Texture2d> lease) {
		this.lease = lease;
		Kind = ResolvedTextureSourceKind.LeasedTexture2d;
	}

	public uint Width => Kind switch {
		ResolvedTextureSourceKind.Texture2d => texture!.Width,
		ResolvedTextureSourceKind.RenderTarget2d => renderTarget!.Width,
		ResolvedTextureSourceKind.LeasedTexture2d => lease.Value.Width,
		_ => throw new UnreachableException(),
	};
	public uint Height => Kind switch {
		ResolvedTextureSourceKind.Texture2d => texture!.Height,
		ResolvedTextureSourceKind.RenderTarget2d => renderTarget!.Height,
		ResolvedTextureSourceKind.LeasedTexture2d => lease.Value.Height,
		_ => throw new UnreachableException(),
	};
	public GpuBindGroupRef BindGroup => Kind switch {
		ResolvedTextureSourceKind.Texture2d => texture!.BindGroup,
		ResolvedTextureSourceKind.RenderTarget2d => renderTarget!.ColorBindGroup,
		ResolvedTextureSourceKind.LeasedTexture2d => lease.Value.BindGroup,
		_ => throw new UnreachableException(),
	};

	public object Identity => Kind switch {
		ResolvedTextureSourceKind.Texture2d => texture!,
		ResolvedTextureSourceKind.RenderTarget2d => renderTarget!,
		ResolvedTextureSourceKind.LeasedTexture2d => lease.Value,
		_ => throw new UnreachableException(),
	};

	// comparing color textures only is enough since every render target has its own one
	public bool SameRenderTargetAs(RenderTarget2d rt) =>
		Kind == ResolvedTextureSourceKind.RenderTarget2d && renderTarget!.ColorTexture.SameTexture(rt.ColorTexture);
}
