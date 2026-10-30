// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Injure.DevAnalyzers.Attributes;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Describes how a <see cref="CanvasScissor"/> is interpreted.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct CanvasScissorKind {
	/// <summary>Raw switch tag for <see cref="CanvasScissorKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// No clipping beyond the drawable area itself.
		/// </summary>
		None,

		/// <summary>
		/// Draws are clipped to the given rectangle, as well as to the drawable area.
		/// </summary>
		Set,
	}
}

/// <summary>
/// A scissor for <see cref="Canvas"/>, i.e. a rectangle of the drawable area that draws are
/// clipped to.
/// </summary>
/// <remarks>
/// <para>
/// The rectangles are in drawable units, in the drawable area of the target's
/// <see cref="CanvasTarget.Viewport"/>, and <b>not affected by
/// <see cref="CanvasParams.Transform"/></b>, since the scissor is render pass state rather than
/// geometry. <see cref="FromTransformed(RectF, Matrix3x2)"/> maps a rectangle through a transform
/// matrix, e.g. to clip to a rectangle drawn under it. Draws are always clipped to the drawable
/// area regardless of the scissor; a scissor can only add additional clipping confined to the
/// drawable area.
/// </para>
/// <para>
/// To clip, the scissor is mapped to target pixels with each edge rounded separately to the
/// nearest pixel boundary, with midpoints rounded towards positive infinity, rather than
/// rounding the position and size. This way, scissors sharing an edge map to adjacent pixel
/// rectangles, with no gap or overlap between them.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </para>
/// </remarks>
public readonly struct CanvasScissor : IEquatable<CanvasScissor> {
	/// <summary>
	/// How <see cref="Rect"/> is interpreted.
	/// </summary>
	public CanvasScissorKind Kind { get; }

	/// <summary>
	/// Scissor rectangle, in drawable units.
	/// </summary>
	/// <remarks>
	/// Ignored for <see cref="CanvasScissorKind.None"/>.
	/// </remarks>
	public RectF Rect { get; }

	private CanvasScissor(CanvasScissorKind kind, RectF rect) {
		Kind = kind;
		Rect = rect;
	}

	/// <summary>
	/// No clipping beyond the drawable area itself.
	/// </summary>
	public static readonly CanvasScissor None = new(CanvasScissorKind.None, default);

	/// <summary>
	/// Creates a scissor that is <paramref name="rect"/>, clamped to the drawable area.
	/// </summary>
	/// <param name="rect">Scissor rectangle, in drawable units.</param>
	public static CanvasScissor Set(RectF rect) => new(CanvasScissorKind.Set, rect);

	// relative to the largest element; rotations by multiples of 90 degrees computed in floats leave
	// the elements that should be 0 around 1e-7, well below this
	private const float axisEpsilon = 1e-5f;

	/// <summary>
	/// Maps <paramref name="rect"/> through <paramref name="transform"/>, which must map axis-aligned
	/// rectangles to axis-aligned rectangles, for use as a scissor rect.
	/// </summary>
	/// <param name="rect">
	/// Rectangle to map, e.g. one that's drawn under <paramref name="transform"/>.
	/// </param>
	/// <param name="transform">
	/// Matrix mapping <paramref name="rect"/> to an axis-aligned rect in the drawable area, e.g.
	/// <see cref="Canvas.CurrentTransform"/>; must be invertible (see <see cref="MatrixInvert"/>).
	/// </param>
	/// <returns>The mapped rectangle.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="transform"/> isn't invertible, or doesn't map axis-aligned
	/// rectangles to axis-aligned rectangles, i.e. rotates by something other than a multiple of 90
	/// degrees or shears.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Scissors ignore <see cref="CanvasParams.Transform"/>, so clipping to a rectangle that's drawn
	/// under a transform needs the rectangle mapped through that transform manually. Pass in the
	/// transform the scissor will apply under; if the same override also changes the transform, make
	/// sure to use the new transform, not the current <see cref="Canvas.CurrentTransform"/>. Negative
	/// scales (mirroring) and rotations by multiples of 90 degrees are allowed; the edges are
	/// reordered as needed.
	/// </para>
	/// <para>
	/// The matrix counts as axis-aligned if either both off-diagonal elements
	/// (<see cref="Matrix3x2.M12"/> and <see cref="Matrix3x2.M21"/>) or both diagonal elements
	/// (<see cref="Matrix3x2.M11"/> and <see cref="Matrix3x2.M22"/>) are 0, with a slight tolerance
	/// for float error relative to the largest of the four.
	/// </para>
	/// </remarks>
	public static RectF FromTransformed(RectF rect, Matrix3x2 transform) {
		MatrixInvert.ThrowIfNotInvertible(transform);
		Matrix3x2 m = transform;
		float tolerance = axisEpsilon * MathF.Max(
			MathF.Max(MathF.Abs(m.M11), MathF.Abs(m.M22)),
			MathF.Max(MathF.Abs(m.M12), MathF.Abs(m.M21))
		);
		float x1, x2, y1, y2;
		// ignore the elements within `tolerance` of 0 rather than multiplying in float noise
		if (MathF.Abs(m.M12) <= tolerance && MathF.Abs(m.M21) <= tolerance) {
			x1 = rect.X * m.M11 + m.M31;
			x2 = rect.Right * m.M11 + m.M31;
			y1 = rect.Y * m.M22 + m.M32;
			y2 = rect.Bottom * m.M22 + m.M32;
		} else if (MathF.Abs(m.M11) <= tolerance && MathF.Abs(m.M22) <= tolerance) {
			x1 = rect.Y * m.M21 + m.M31;
			x2 = rect.Bottom * m.M21 + m.M31;
			y1 = rect.X * m.M12 + m.M32;
			y2 = rect.Right * m.M12 + m.M32;
		} else {
			throw new ArgumentOutOfRangeException(nameof(transform), transform, "transform must map axis-aligned rectangles to axis-aligned rectangles");
		}
		return RectF.FromLtrb(MathF.Min(x1, x2), MathF.Min(y1, y2), MathF.Max(x1, x2), MathF.Max(y1, y2));
	}

	public bool Equals(CanvasScissor other) => Kind == other.Kind && Rect.Equals(other.Rect);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasScissor other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Kind.RawTag, Rect);
	public static bool operator ==(CanvasScissor left, CanvasScissor right) => left.Equals(right);
	public static bool operator !=(CanvasScissor left, CanvasScissor right) => !left.Equals(right);
}

/// <summary>
/// Describes how a <see cref="CanvasScissorOverride"/> is applied to the current scissor.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct CanvasScissorOverrideKind {
	/// <summary>Raw switch tag for <see cref="CanvasScissorOverrideKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// Removes the current scissor; draws are only clipped to the drawable area.
		/// </summary>
		NoScissor = 1,

		/// <summary>
		/// Replaces the current scissor with the given rectangle, clamped to the drawable area.
		/// </summary>
		Set,

		/// <summary>
		/// Intersects the current scissor with the given rectangle.
		/// </summary>
		Intersect,
	}
}

/// <summary>
/// A change to the current scissor, for <see cref="CanvasParamsOverride.Scissor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The rectangles are in drawable units, unaffected by the transform matrix, and cannot widen the
/// clip beyond the drawable area; see <see cref="CanvasScissor"/> for more info.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasScissorOverride : IEquatable<CanvasScissorOverride>, IHasInvalidDefault {
	private readonly CanvasScissorOverrideKind kind;
	private readonly RectF rect;

	/// <summary>
	/// How <see cref="Rect"/> is applied to the current scissor.
	/// </summary>
	public CanvasScissorOverrideKind Kind => IsDefault ? throw InvalidDefaultException.For<CanvasScissorOverride>() : kind;

	/// <summary>
	/// Rectangle to set or intersect with, in drawable units.
	/// </summary>
	/// <remarks>
	/// Ignored for <see cref="CanvasScissorOverrideKind.NoScissor"/>.
	/// </remarks>
	public RectF Rect => IsDefault ? throw InvalidDefaultException.For<CanvasScissorOverride>() : rect;

	private CanvasScissorOverride(CanvasScissorOverrideKind kind, RectF rect) {
		this.kind = kind;
		this.rect = rect;
	}

	/// <summary>
	/// Removes the current scissor, making draws only clipped to the drawable area.
	/// </summary>
	public static readonly CanvasScissorOverride NoScissor = new(CanvasScissorOverrideKind.NoScissor, default);

	/// <summary>
	/// Creates an override that replaces the current scissor with <paramref name="rect"/>, clamped
	/// to the drawable area.
	/// </summary>
	/// <param name="rect">Scissor rectangle, in drawable units.</param>
	public static CanvasScissorOverride Set(RectF rect) => new(CanvasScissorOverrideKind.Set, rect);

	/// <summary>
	/// Creates an override that intersects the current scissor with <paramref name="rect"/>.
	/// </summary>
	/// <param name="rect">
	/// Rectangle to intersect the current scissor with, in drawable units.
	/// </param>
	/// <remarks>
	/// If the current scissor is <see cref="CanvasScissor.None"/>, this is the same as
	/// <see cref="Set(RectF)"/>. If the rectangles don't overlap with a positive area, the new
	/// scissor rectangle is <see cref="RectF.Empty"/>, which clips every draw (see
	/// <see cref="RectF.TryIntersect(RectF, out RectF)"/>).
	/// </remarks>
	public static CanvasScissorOverride Intersect(RectF rect) => new(CanvasScissorOverrideKind.Intersect, rect);

	public bool Equals(CanvasScissorOverride other) => kind == other.kind && rect.Equals(other.rect);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasScissorOverride other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(kind.RawTag, rect);
	public static bool operator ==(CanvasScissorOverride left, CanvasScissorOverride right) => left.Equals(right);
	public static bool operator !=(CanvasScissorOverride left, CanvasScissorOverride right) => !left.Equals(right);

	internal bool IsDefault => kind == default;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
