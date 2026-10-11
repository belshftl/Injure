// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Where a texture drawn with <see cref="Canvas.Texture(TextureSource, in TexDraw)"/> goes:
/// anchored at a position at its natural size, or stretched into a rectangle.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Vector2"/> converts to <see cref="At(Vector2)"/> and <see cref="RectF"/> to
/// <see cref="Rect(RectF)"/> implicitly.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct TexDst : IEquatable<TexDst> {
	private enum Kind : byte {
		Invalid,
		At,
		Rect,
	}

	private readonly Kind kind;
	private readonly RectF rect; // only X and Y are used for At

	private TexDst(Kind kind, RectF rect) {
		this.kind = kind;
		this.rect = rect;
	}

	internal bool IsDefault => kind == Kind.Invalid;

	/// <summary>
	/// Draws with <see cref="TexDraw.Origin"/> (the top-left corner by default) at
	/// <paramref name="position"/>, at the natural size of the source region in texture pixels.
	/// </summary>
	/// <param name="position">Where the origin point goes.</param>
	public static TexDst At(Vector2 position) => new(Kind.At, new RectF(position.X, position.Y, 0f, 0f));

	/// <summary>
	/// Draws the source region stretched into <paramref name="rect"/>; <see cref="TexDraw.Scale"/> and
	/// <see cref="TexDraw.Rotation"/> then apply around <see cref="TexDraw.Origin"/> inside it.
	/// </summary>
	/// <param name="rect">Destination rectangle.</param>
	public static TexDst Rect(RectF rect) => new(Kind.Rect, rect);

	/// <summary>
	/// Creates a destination anchored at <paramref name="position"/>; see <see cref="At(Vector2)"/>.
	/// </summary>
	public static implicit operator TexDst(Vector2 position) => At(position);

	/// <summary>
	/// Creates a destination stretching into <paramref name="rect"/>; see <see cref="Rect(RectF)"/>.
	/// </summary>
	public static implicit operator TexDst(RectF rect) => Rect(rect);

	// returns where the origin point goes and the quad's size before scaling; the natural size is the
	// source region's size in texture pixels, and the origin is normalized to the quad
	internal (Vector2 Pivot, Vector2 Size) Resolve(float naturalWidth, float naturalHeight, Vector2 origin) => kind switch {
		Kind.At => (new Vector2(rect.X, rect.Y), new Vector2(naturalWidth, naturalHeight)),
		Kind.Rect => (new Vector2(rect.X + origin.X * rect.Width, rect.Y + origin.Y * rect.Height), new Vector2(rect.Width, rect.Height)),
		_ => throw new UnreachableException(),
	};

	public bool Equals(TexDst other) => kind == other.kind && rect.Equals(other.rect);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is TexDst other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(kind, rect);
	public static bool operator ==(TexDst left, TexDst right) => left.Equals(right);
	public static bool operator !=(TexDst left, TexDst right) => !left.Equals(right);
}

/// <summary>
/// Which part of a texture <see cref="Canvas.Texture(TextureSource, in TexDraw)"/> draws: all of
/// it, a rectangle in texture pixels, or a rectangle in UV coordinates.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Whole"/>.
/// </remarks>
public readonly struct TexRegion : IEquatable<TexRegion> {
	private enum Kind : byte {
		Whole,
		Pixels,
		Uv,
	}

	private readonly Kind kind;
	private readonly RectF rect;

	private TexRegion(Kind kind, RectF rect) {
		this.kind = kind;
		this.rect = rect;
	}

	/// <summary>
	/// The whole texture.
	/// </summary>
	public static readonly TexRegion Whole = default;

	/// <summary>
	/// A rectangle of the texture, in texture pixels.
	/// </summary>
	/// <param name="rect">The rectangle, in texture pixels.</param>
	public static TexRegion Pixels(RectF rect) => new(Kind.Pixels, rect);

	/// <summary>
	/// A rectangle of the texture, in UV coordinates, i.e. from 0 to 1 across the texture.
	/// </summary>
	/// <param name="rect">The rectangle, in UV coordinates.</param>
	public static TexRegion Uv(RectF rect) => new(Kind.Uv, rect);

	// returns the region in UV coordinates and its natural size in texture pixels
	internal (RectF Uv, float Width, float Height) Resolve(uint textureWidth, uint textureHeight) {
		switch (kind) {
		case Kind.Whole:
			return (new RectF(0f, 0f, 1f, 1f), textureWidth, textureHeight);
		case Kind.Pixels: {
			float invW = 1f / textureWidth;
			float invH = 1f / textureHeight;
			return (new RectF(rect.X * invW, rect.Y * invH, rect.Width * invW, rect.Height * invH), rect.Width, rect.Height);
		}
		case Kind.Uv:
			return (rect, rect.Width * textureWidth, rect.Height * textureHeight);
		default:
			throw new UnreachableException();
		}
	}

	public bool Equals(TexRegion other) => kind == other.kind && rect.Equals(other.rect);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is TexRegion other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(kind, rect);
	public static bool operator ==(TexRegion left, TexRegion right) => left.Equals(right);
	public static bool operator !=(TexRegion left, TexRegion right) => !left.Equals(right);
}

/// <summary>
/// How <see cref="Canvas.Texture(TextureSource, in TexDraw)"/> draws a texture: where, which part
/// of it, how it's transformed, and with what tint.
/// </summary>
/// <param name="Destination">Where the texture goes.</param>
/// <remarks>
/// <para>
/// <see cref="Vector2"/> (a top-left position at natural size) and <see cref="RectF"/> (a
/// destination rectangle) convert to <see cref="TexDraw"/> implicitly, so the common cases are
/// <c>cv.Texture(tex, pos)</c> and <c>cv.Texture(tex, rect)</c>.
/// </para>
/// <para>
/// The quad is placed as described by <paramref name="Destination"/>, then scaled by
/// <see cref="Scale"/> and rotated by <see cref="Rotation"/>, both around <see cref="Origin"/>.
/// <see cref="FlipX"/> and <see cref="FlipY"/> mirror the texture within the quad without moving
/// it. All of this happens before <see cref="CanvasParams.Transform"/>, which applies to the result
/// like to any other draw.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <paramref name="Destination"/> is.
/// </para>
/// </remarks>
public readonly record struct TexDraw(TexDst Destination) {
	/// <summary>
	/// Which part of the texture to draw; <see cref="TexRegion.Whole"/> unless set.
	/// </summary>
	public TexRegion Source { get; init; } = TexRegion.Whole;

	/// <summary>
	/// Color multiplied with the sampled texels; <see cref="SrgbColor32.White"/>, i.e. no tint, unless
	/// set.
	/// </summary>
	public SrgbColor32 Tint { get; init; } = SrgbColor32.White;

	/// <summary>
	/// The point that the quad is anchored at, scaled around, and rotated around, normalized to the
	/// quad: (0, 0) is the top-left corner, (1, 1) the bottom-right, and (0.5, 0.5) the center. (0, 0)
	/// unless set.
	/// </summary>
	/// <remarks>
	/// Values outside of [0, 1] are allowed, and put the origin outside the quad.
	/// </remarks>
	public Vector2 Origin { get; init; } = Vector2.Zero;

	/// <summary>
	/// Factor the quad's size is multiplied with, around <see cref="Origin"/>; (1, 1) unless set.
	/// </summary>
	public Vector2 Scale { get; init; } = Vector2.One;

	/// <summary>
	/// Rotation around <see cref="Origin"/>, in radians; 0 unless set.
	/// </summary>
	/// <remarks>
	/// Positive values rotate clockwise on the target, since +Y points down.
	/// </remarks>
	public float Rotation { get; init; } = 0f;

	/// <summary>
	/// Whether to mirror the texture horizontally within the quad.
	/// </summary>
	public bool FlipX { get; init; }

	/// <summary>
	/// Whether to mirror the texture vertically within the quad.
	/// </summary>
	public bool FlipY { get; init; }

	/// <summary>
	/// Draws the whole texture untinted and untransformed with its top-left corner at
	/// <paramref name="topLeft"/>, at its natural size.
	/// </summary>
	public static implicit operator TexDraw(Vector2 topLeft) => new(TexDst.At(topLeft));

	/// <summary>
	/// Draws the whole texture untinted and untransformed, stretched into <paramref name="dst"/>.
	/// </summary>
	public static implicit operator TexDraw(RectF dst) => new(TexDst.Rect(dst));
}
