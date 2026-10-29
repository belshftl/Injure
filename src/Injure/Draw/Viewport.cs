// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Injure.Host;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// The drawable area of a target, represented as a uniform scale + a translation + how large the
/// area is in drawable units.
/// </summary>
/// <remarks>
/// <para>
/// A drawable unit is a square of <see cref="PixelsPerUnit"/> by <see cref="PixelsPerUnit"/>
/// target pixels. The top left of the drawable area is <see cref="PixelOffset"/>, and the area is
/// <see cref="Size"/> drawable units large; positions in it are in drawable units from its top
/// left.
/// </para>
/// <para>
/// Typical viewports are <see cref="Whole(Vector2, float)"/> of the target, at a scale of 1 for
/// physical pixels or the window's pixel density for logical units, and
/// <see cref="Letterbox(Vector2, Vector2, bool)"/> for a fixed-size area.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <see cref="PixelsPerUnit"/> is 0.
/// </para>
/// </remarks>
public readonly struct Viewport : IEquatable<Viewport>, IHasInvalidDefault {
	/// <summary>
	/// The side length of one drawable unit, in target pixels. Always positive and finite.
	/// </summary>
	public float PixelsPerUnit { get; }

	/// <summary>
	/// Where the drawable area's top left is, in target pixels.
	/// </summary>
	public Vector2 PixelOffset { get; }

	/// <summary>
	/// The drawable area's size, in drawable units. Always non-negative and finite.
	/// </summary>
	public Vector2 Size { get; }

	/// <summary>
	/// Creates a viewport.
	/// </summary>
	/// <param name="pixelOffset">Where the drawable area's top left is, in target pixels.</param>
	/// <param name="pixelsPerUnit">The side length of one drawable unit, in target pixels.</param>
	/// <param name="size">The drawable area's size, in drawable units.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="pixelOffset"/> isn't finite, <paramref name="pixelsPerUnit"/> isn't
	/// positive and finite, or <paramref name="size"/> isn't non-negative and finite.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the drawable area's bottom right in pixels isn't finite, or the matrix mapping
	/// drawable units to pixels isn't invertible (see <see cref="MatrixInvert"/>), e.g. at an
	/// extremely small <paramref name="pixelsPerUnit"/>.
	/// </exception>
	public Viewport(Vector2 pixelOffset, float pixelsPerUnit, Vector2 size) {
		if (!float.IsFinite(pixelOffset.X) || !float.IsFinite(pixelOffset.Y))
			throw new ArgumentOutOfRangeException(nameof(pixelOffset), pixelOffset, "pixel offset must be finite");
		if (!float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0f)
			throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), pixelsPerUnit, "pixels per unit must be positive and finite");
		if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X < 0f || size.Y < 0f)
			throw new ArgumentOutOfRangeException(nameof(size), size, "size components must be non-negative and finite");
		Vector2 pixelEnd = pixelOffset + size * pixelsPerUnit;
		if (!float.IsFinite(pixelEnd.X) || !float.IsFinite(pixelEnd.Y))
			throw new ArgumentException($"the drawable area's bottom right in pixels ({pixelOffset} + {size} * {pixelsPerUnit}) isn't finite");
		PixelOffset = pixelOffset;
		PixelsPerUnit = pixelsPerUnit;
		Size = size;
		if (!MatrixInvert.IsInvertible(ToPixelsMatrix))
			throw new ArgumentException($"a viewport at {pixelsPerUnit} pixels per unit and an offset of {pixelOffset} doesn't map to pixels invertibly");
	}

	/// <summary>
	/// Creates a viewport covering a whole target of size <paramref name="targetPixelSize"/>.
	/// </summary>
	/// <param name="targetPixelSize">Size of the target, in pixels.</param>
	/// <param name="pixelsPerUnit">
	/// The side length of one drawable unit, in target pixels, e.g. 1 for physical pixels, or the
	/// window's pixel density for logical units.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if a component of <paramref name="targetPixelSize"/> isn't non-negative and finite, or
	/// <paramref name="pixelsPerUnit"/> isn't positive and finite.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the matrix mapping drawable units to pixels isn't invertible (see
	/// <see cref="MatrixInvert"/>).
	/// </exception>
	/// <remarks>
	/// The drawable area's size is <c>targetPixelSize / pixelsPerUnit</c>, which can be fractional.
	/// </remarks>
	public static Viewport Whole(Vector2 targetPixelSize, float pixelsPerUnit = 1f) {
		if (!float.IsFinite(targetPixelSize.X) || !float.IsFinite(targetPixelSize.Y) || targetPixelSize.X < 0f || targetPixelSize.Y < 0f)
			throw new ArgumentOutOfRangeException(nameof(targetPixelSize), targetPixelSize, "size components must be non-negative and finite");
		if (!float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0f)
			throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), pixelsPerUnit, "pixels per unit must be positive and finite");
		return new Viewport(Vector2.Zero, pixelsPerUnit, targetPixelSize / pixelsPerUnit);
	}

	/// <summary>
	/// Creates a viewport that fits a drawable area of size <paramref name="contentSize"/> into a
	/// target of size <paramref name="targetPixelSize"/>, scaled uniformly as large as fits and
	/// centered, leaving bars at the top/bottom or the sides if the aspect ratios differ.
	/// </summary>
	/// <param name="contentSize">
	/// Size of the drawable area, in drawable units, e.g. a fixed logical resolution.
	/// </param>
	/// <param name="targetPixelSize">Size of the target to fit into, in pixels.</param>
	/// <param name="integerScale">
	/// Whether to round <see cref="PixelsPerUnit"/> towards negative infinity to a whole number,
	/// allowing for a clean integer upscale at the cost of leaving wider bars, and potentially having
	/// bars on all four sides. Ignored if the content doesn't fit at 1 pixel per drawable unit.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if a component of <paramref name="contentSize"/> or <paramref name="targetPixelSize"/>
	/// isn't positive and finite.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the sizes are so far apart that the resulting viewport isn't valid.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Technically not a strict letterbox, since it may pillarbox too. Injure uses "letterbox" to
	/// mean both, since the term "pillarbox" is more obscure and the distinction is irrelevant in
	/// this case.
	/// </para>
	/// <para>
	/// <see cref="PixelOffset"/> is rounded towards negative infinity to whole pixels, so with
	/// <paramref name="integerScale"/>, drawable units land exactly on target pixels. Values within
	/// float error of a whole pixel are snapped to said whole pixel.
	/// </para>
	/// </remarks>
	public static Viewport Letterbox(Vector2 contentSize, Vector2 targetPixelSize, bool integerScale) {
		// snaps x to an integer within float error of it, relative to the target size it was computed
		// from, then floors it; otherwise e.g. 239.99994 floors to 239 and ends up a whole pixel off
		// center when it should be 240
		static float snapFloor(float x, float magnitude) {
			const float epsilon = 1e-5f;
			float r = MathF.Round(x);
			return MathF.Abs(x - r) <= epsilon * MathF.Max(1f, magnitude) ? r : MathF.Floor(x);
		}

		chkPositive(contentSize, nameof(contentSize));
		chkPositive(targetPixelSize, nameof(targetPixelSize));
		float scale = MathF.Min(targetPixelSize.X / contentSize.X, targetPixelSize.Y / contentSize.Y);
		if (integerScale && scale >= 1f)
			scale = MathF.Floor(scale);
		Vector2 pixelSize = contentSize * scale;
		Vector2 offset = new(
			snapFloor((targetPixelSize.X - pixelSize.X) / 2f, targetPixelSize.X),
			snapFloor((targetPixelSize.Y - pixelSize.Y) / 2f, targetPixelSize.Y)
		);
		return new Viewport(offset, scale, contentSize);
	}

	/// <summary>
	/// The drawable area, in target pixels.
	/// </summary>
	public RectF PixelRect => new(PixelOffset.X, PixelOffset.Y, Size.X * ppu, Size.Y * ppu);

	/// <summary>
	/// The matrix mapping positions in the drawable area, in drawable units, to target pixels. Always
	/// invertible (see <see cref="MatrixInvert"/>).
	/// </summary>
	public Matrix3x2 ToPixelsMatrix => Matrix3x2.CreateScale(ppu) * Matrix3x2.CreateTranslation(PixelOffset);

	/// <summary>
	/// Maps a position in the drawable area, in drawable units, to target pixels.
	/// </summary>
	/// <param name="pos">Position in drawable units.</param>
	public Vector2 ToPixels(Vector2 pos) => Vector2.FusedMultiplyAdd(pos, Vector2.One * ppu, PixelOffset);

	/// <summary>
	/// Maps a position in target pixels to the drawable area, in drawable units.
	/// </summary>
	/// <param name="pixelPos">Position in target pixels.</param>
	/// <remarks>
	/// Positions outside of the drawable area, e.g. in letterbox bars, map to positions outside of
	/// [0, <see cref="Size"/>).
	/// </remarks>
	public Vector2 FromPixels(Vector2 pixelPos) => (pixelPos - PixelOffset) / ppu;

	/// <summary>
	/// Maps a rectangle in the drawable area, in drawable units, to target pixels.
	/// </summary>
	/// <param name="rect">Rectangle in drawable units.</param>
	public RectF ToPixels(RectF rect) {
		Vector2 p = ToPixels(rect.Position);
		return new RectF(p.X, p.Y, rect.Width * ppu, rect.Height * ppu);
	}

	/// <summary>
	/// Maps a position in window coordinates, e.g. a pointer position, to the drawable area, in
	/// drawable units.
	/// </summary>
	/// <param name="pos">Position in window coordinates.</param>
	/// <param name="pixelDensity">
	/// The amount of pixels per window coordinate for the window, e.g.
	/// <see cref="Sdl.SdlWindowState.PixelDensity"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="pixelDensity"/> isn't positive and finite.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Assumes that this viewport's target covers the whole window at the window's size in pixels,
	/// which is the case for a window's own surface. An embedded surface covering only part of a
	/// window needs its own offset applied first.
	/// </para>
	/// <para>
	/// The result is in the drawable area's coordinates, i.e. after <see cref="CanvasParams.Transform"/>.
	/// To get draw coordinates under a transform, map the result through the inverse of that transform.
	/// </para>
	/// </remarks>
	public Vector2 FromWindow(WindowPos pos, float pixelDensity) {
		chkDensity(pixelDensity);
		return FromPixels(pos.ToRaw() * pixelDensity);
	}

	/// <summary>
	/// Maps a movement in window coordinates, e.g. a pointer movement, to drawable units.
	/// </summary>
	/// <param name="delta">Movement in window coordinates.</param>
	/// <param name="pixelDensity">
	/// The amount of pixels per window coordinate for the window, e.g.
	/// <see cref="Sdl.SdlWindowState.PixelDensity"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="pixelDensity"/> isn't positive and finite.
	/// </exception>
	public Vector2 FromWindow(WindowDelta delta, float pixelDensity) {
		chkDensity(pixelDensity);
		return delta.ToRaw() * pixelDensity / ppu;
	}

	// mapping through the default value would divide by or multiply with 0
	private float ppu => IsDefault ? throw InvalidDefaultException.For<Viewport>() : PixelsPerUnit;

	/// <summary>
	/// Rounds each edge separately to the nearest pixel boundary, with midpoints rounded towards
	/// positive infinity, so that adjacent rects don't stop being adjacent. Edges past 2^24 are
	/// clamped, which practically doesn't matter because render targets never get that large.
	/// </summary>
	internal static RectI RoundToPixels(RectF rect) => RoundToPixels(rect.Left, rect.Top, rect.Right, rect.Bottom);

	/// <inheritdoc cref="RoundToPixels(RectF)" path="/summary"/>
	/// <remarks>
	/// Takes the edges as floats, which may be infinite, rather than as a <see cref="RectF"/>, which
	/// can't be.
	/// </remarks>
	internal static RectI RoundToPixels(float left, float top, float right, float bottom) {
		const float edgeLimit = 1 << 24;
		static int edge(float v) => (int)MathF.Floor(Math.Clamp(v, -edgeLimit, edgeLimit) + 0.5f);
		int l = edge(left), r = edge(right), t = edge(top), b = edge(bottom);
		return new RectI(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
	}

	private static void chkPositive(Vector2 size, string paramName) {
		if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0f || size.Y <= 0f)
			throw new ArgumentOutOfRangeException(paramName, size, "size components must be positive and finite");
	}

	private static void chkDensity(float pixelDensity) {
		if (!float.IsFinite(pixelDensity) || pixelDensity <= 0f)
			throw new ArgumentOutOfRangeException(nameof(pixelDensity), pixelDensity, "pixel density must be positive and finite");
	}

	public bool Equals(Viewport other) => PixelOffset == other.PixelOffset && PixelsPerUnit == other.PixelsPerUnit && Size == other.Size;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is Viewport other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(PixelOffset, PixelsPerUnit, Size);
	public static bool operator ==(Viewport left, Viewport right) => left.Equals(right);
	public static bool operator !=(Viewport left, Viewport right) => !left.Equals(right);

	internal bool IsDefault => PixelsPerUnit == 0f;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
