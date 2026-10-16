// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Injure.DevAnalyzers.Attributes;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Target for a <see cref="Canvas"/> to draw into (either the frame's primary output or an
/// offscreen <see cref="RenderTarget2d"/>), plus a view matrix mapping the target's logical space
/// to its pixels.
/// </summary>
/// <remarks>
/// <para>
/// Draws are transformed by <see cref="CanvasParams.Transform"/> first and by <see cref="View"/>
/// second, i.e. the effective transform is <c>transform * view</c> (<see cref="System.Numerics"/>
/// matrices act on row vectors, so the left operand applies first). The view matrix belongs to the
/// target rather than to the transform, so <see cref="CanvasTransformOverride.Set(Matrix3x2)"/>
/// replaces only the transform, and the view matrix stays. A typical view matrix for the primary
/// output scales by the window's pixel density, so that draws are in logical units rather than
/// physical pixels. The view matrix must be invertible (see <see cref="MatrixInvert"/>).
/// </para>
/// <para>
/// <see cref="RenderTarget2d"/> converts to <see cref="CanvasTarget"/> implicitly, using an
/// identity matrix for the view matrix, since it's very uncommon that a non-identity view matrix
/// is used for an offscreen render target.
/// </para>
/// <para>
/// Equality compares both the render target and the view matrix; see
/// <see cref="SameRenderTarget(CanvasTarget)"/> if you only care about the render target.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasTarget : IEquatable<CanvasTarget> {
	private readonly RenderTarget2d? rt;
	private readonly bool isPrimary;
	private readonly Matrix3x2 view;

	/// <summary>
	/// Matrix applied to draws after <see cref="CanvasParams.Transform"/>, mapping this target's
	/// logical space to its pixels. Always invertible.
	/// </summary>
	public Matrix3x2 View => IsDefault ? throw InvalidDefaultException.For<CanvasTarget>() : view;

	/// <summary>
	/// Whether this is the frame's primary output; see <see cref="RenderFrame.PrimaryView"/>.
	/// </summary>
	[MemberNotNullWhen(false, nameof(rt))]
	public bool IsPrimary {
		get {
			if (IsDefault)
				throw InvalidDefaultException.For<CanvasTarget>();
			return isPrimary;
		}
	}

	/// <summary>
	/// The render target this refers to.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this is the primary output.
	/// </exception>
	public RenderTarget2d RenderTarget => IsPrimary
		? throw new InvalidOperationException("primary target has no RenderTarget2d")
		: rt;

	internal bool IsDefault => rt is null && !isPrimary;

	private CanvasTarget(RenderTarget2d? rt, bool isPrimary, Matrix3x2 view) {
		this.rt = rt;
		this.isPrimary = isPrimary;
		this.view = view;
	}

	/// <summary>
	/// Creates a target referring to the frame's primary output; see
	/// <see cref="RenderFrame.PrimaryView"/>.
	/// </summary>
	/// <param name="view">
	/// Matrix applied to draws after <see cref="CanvasParams.Transform"/>; must be
	/// invertible (see <see cref="MatrixInvert"/>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="view"/> isn't invertible.
	/// </exception>
	public static CanvasTarget Primary(Matrix3x2 view) {
		MatrixInvert.ThrowIfNotInvertible(view);
		return new CanvasTarget(null, true, view);
	}

	/// <summary>
	/// Creates a target referring to an offscreen render target.
	/// </summary>
	/// <param name="renderTarget">Render target to draw into.</param>
	/// <param name="view">
	/// Matrix applied to draws after <see cref="CanvasParams.Transform"/>; must be
	/// invertible (see <see cref="MatrixInvert"/>).
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="renderTarget"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="view"/> isn't invertible.
	/// </exception>
	public static CanvasTarget Offscreen(RenderTarget2d renderTarget, Matrix3x2 view) {
		ArgumentNullException.ThrowIfNull(renderTarget);
		MatrixInvert.ThrowIfNotInvertible(view);
		return new CanvasTarget(renderTarget, false, view);
	}

	/// <summary>
	/// Whether this and <paramref name="other"/> refer to the same render target, or both to the
	/// primary output, regardless of their view matrices.
	/// </summary>
	/// <exception cref="InvalidDefaultException">
	/// Thrown if this or <paramref name="other"/> is the <see langword="default"/> value.
	/// </exception>
	/// <remarks>
	/// Changing between targets for which this is <see langword="true"/> doesn't end the render
	/// pass; see <see cref="CanvasParams"/>.
	/// </remarks>
	public bool SameRenderTarget(CanvasTarget other) =>
		IsPrimary == other.IsPrimary && ReferenceEquals(rt, other.rt);

	public bool Equals(CanvasTarget other) => isPrimary == other.isPrimary && ReferenceEquals(rt, other.rt) && view.Equals(other.view);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasTarget other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(rt is null ? 0 : RuntimeHelpers.GetHashCode(rt), isPrimary, view);
	public static bool operator ==(CanvasTarget left, CanvasTarget right) => left.Equals(right);
	public static bool operator !=(CanvasTarget left, CanvasTarget right) => !left.Equals(right);

	/// <summary>
	/// Creates a target referring to <paramref name="target"/>, with the identity view matrix.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="target"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator CanvasTarget(RenderTarget2d target) => Offscreen(target, Matrix3x2.Identity);
}

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
		/// No clipping; the scissor is the whole target.
		/// </summary>
		None,

		/// <summary>
		/// The scissor is the given rectangle, clamped to the target.
		/// </summary>
		Set,
	}
}

/// <summary>
/// A scissor for <see cref="Canvas"/>, i.e. the rectangle of the target that draws are clipped to.
/// </summary>
/// <remarks>
/// <para>
/// Rectangles are in target pixels, and not affected by <see cref="CanvasParams.Transform"/> or
/// <see cref="CanvasTarget.View"/>, since the scissor is render pass state rather than geometry.
/// <see cref="FromTransformed(RectF, Matrix3x2)"/> maps a rectangle in draw coordinates to target
/// pixels.
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
	/// Scissor rectangle, in target pixels.
	/// </summary>
	/// <remarks>
	/// Ignored for <see cref="CanvasScissorKind.None"/>.
	/// </remarks>
	public RectI Rect { get; }

	private CanvasScissor(CanvasScissorKind kind, RectI rect) {
		Kind = kind;
		Rect = rect;
	}

	/// <summary>
	/// No clipping; the scissor is the whole target.
	/// </summary>
	public static readonly CanvasScissor None = new(CanvasScissorKind.None, default);

	/// <summary>
	/// Creates a scissor that is <paramref name="rect"/>, clamped to the target.
	/// </summary>
	/// <param name="rect">Scissor rectangle, in target pixels.</param>
	public static CanvasScissor Set(RectI rect) => new(CanvasScissorKind.Set, rect);

	// relative to the largest element; rotations by multiples of 90 degrees computed in floats leave
	// the elements that should be 0 around 1e-7, well below this
	private const float axisEpsilon = 1e-5f;
	// floats can't represent every integer past 2^24 anyway, and no target is that large
	private const float edgeLimit = 1 << 24;

	/// <summary>
	/// Maps <paramref name="rect"/> through <paramref name="transform"/> to a rectangle in target
	/// pixels, for use as a scissor rect.
	/// </summary>
	/// <param name="rect">Rectangle to map, e.g. in the coordinates draws are made in.</param>
	/// <param name="transform">
	/// Matrix mapping <paramref name="rect"/> to an axis-aligned rect in target pixels, e.g.
	/// <see cref="Canvas.EffectiveTransform"/>; must be invertible (see <see cref="MatrixInvert"/>).
	/// </param>
	/// <returns>
	/// The mapped rectangle, with each edge rounded to the nearest pixel boundary.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="transform"/> isn't invertible, or doesn't map axis-aligned
	/// rectangles to axis-aligned rectangles, i.e. rotates by something other than a multiple of 90
	/// degrees, or shears.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Scissors are in target pixels and ignore <see cref="CanvasParams.Transform"/> and
	/// <see cref="CanvasTarget.View"/>, so a rectangle in draw coords needs to be manually mapped to
	/// target pixels using the aforementioned matrices. Pass in the matrix the scissor will apply
	/// under; if the same override also changes the transform or the target, make sure to use the new
	/// effective transform, not the current <see cref="Canvas.EffectiveTransform"/>.
	/// </para>
	/// <para>
	/// Each edge is rounded separately, with halves rounded up (towards positive infinity), rather
	/// than rounding the position and size. This way, adjacent rectangles sharing an edge map to
	/// adjacent pixel rectangles, with no gap or overlap between them. Negative scales (flips) and
	/// rotations by multiples of 90 degrees are allowed; the edges are reordered as needed.
	/// </para>
	/// <para>
	/// The matrix counts as axis-aligned if either both off-diagonal elements
	/// (<see cref="Matrix3x2.M12"/> and <see cref="Matrix3x2.M21"/>) or both diagonal elements
	/// (<see cref="Matrix3x2.M11"/> and <see cref="Matrix3x2.M22"/>) are 0, with a slight tolerance
	/// for float error relative to the largest of the four. Edges further than 2^24 pixels from the
	/// origin are clamped to that distance, which doesn't change the effective scissor, since it's
	/// clamped to the target and render targets pretty much never get that huge.
	/// </para>
	/// </remarks>
	public static RectI FromTransformed(RectF rect, Matrix3x2 transform) {
		static int roundEdge(float v) => (int)MathF.Floor(Math.Clamp(v, -edgeLimit, edgeLimit) + 0.5f);

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
		int left = roundEdge(MathF.Min(x1, x2));
		int right = roundEdge(MathF.Max(x1, x2));
		int top = roundEdge(MathF.Min(y1, y2));
		int bottom = roundEdge(MathF.Max(y1, y2));
		return new RectI(left, top, right - left, bottom - top);
	}

	public bool Equals(CanvasScissor other) => Kind == other.Kind && Rect.Equals(other.Rect);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasScissor other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)Kind.Tag, Rect);
	public static bool operator ==(CanvasScissor left, CanvasScissor right) => left.Equals(right);
	public static bool operator !=(CanvasScissor left, CanvasScissor right) => !left.Equals(right);
}

/// <summary>
/// Describes how a <see cref="CanvasScissorOverride"/> is applied to the current scissor.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct CanvasScissorOverrideKind {
	/// <summary>Raw switch tag for <see cref="CanvasScissorOverrideKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// Removes the current scissor; the scissor becomes the whole target.
		/// </summary>
		None,

		/// <summary>
		/// Replaces the current scissor with the given rectangle, clamped to the target.
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
/// Rectangles are in target pixels, like <see cref="CanvasScissor.Rect"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </para>
/// </remarks>
public readonly struct CanvasScissorOverride : IEquatable<CanvasScissorOverride> {
	/// <summary>
	/// How <see cref="Rect"/> is applied to the current scissor.
	/// </summary>
	public CanvasScissorOverrideKind Kind { get; }

	/// <summary>
	/// Rectangle to set or intersect with, in target pixels.
	/// </summary>
	/// <remarks>
	/// Ignored for <see cref="CanvasScissorOverrideKind.None"/>.
	/// </remarks>
	public RectI Rect { get; }

	private CanvasScissorOverride(CanvasScissorOverrideKind kind, RectI rect) {
		Kind = kind;
		Rect = rect;
	}

	/// <summary>
	/// Removes the current scissor; the scissor becomes the whole target.
	/// </summary>
	public static readonly CanvasScissorOverride None = new(CanvasScissorOverrideKind.None, default);

	/// <summary>
	/// Creates an override that replaces the current scissor with <paramref name="rect"/>, clamped
	/// to the target.
	/// </summary>
	/// <param name="rect">Scissor rectangle, in target pixels.</param>
	public static CanvasScissorOverride Set(RectI rect) => new(CanvasScissorOverrideKind.Set, rect);

	/// <summary>
	/// Creates an override that intersects the current scissor with <paramref name="rect"/>.
	/// </summary>
	/// <param name="rect">
	/// Rectangle to intersect the current scissor with, in target pixels.
	/// </param>
	/// <remarks>
	/// If the current scissor is <see cref="CanvasScissor.None"/>, this is the same as
	/// <see cref="Set(RectI)"/>. If the rectangles don't overlap with a positive area, the new
	/// scissor rectangle is <see cref="RectI.Empty"/>, which clips every draw (see
	/// <see cref="RectI.TryIntersect(RectI, out RectI)"/>).
	/// </remarks>
	public static CanvasScissorOverride Intersect(RectI rect) => new(CanvasScissorOverrideKind.Intersect, rect);

	public bool Equals(CanvasScissorOverride other) => Kind == other.Kind && Rect.Equals(other.Rect);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasScissorOverride other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)Kind.Tag, Rect);
	public static bool operator ==(CanvasScissorOverride left, CanvasScissorOverride right) => left.Equals(right);
	public static bool operator !=(CanvasScissorOverride left, CanvasScissorOverride right) => !left.Equals(right);
}

/// <summary>
/// Describes how a <see cref="CanvasTransformOverride"/> is applied to the current transform.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Set"/>.
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // plain replacement is the simplest of the two
public readonly partial struct CanvasTransformOverrideKind {
	/// <summary>Raw switch tag for <see cref="CanvasTransformOverrideKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// Replaces the current transform with the given matrix.
		/// </summary>
		Set,

		/// <summary>
		/// Applies the given matrix in the space of the current transform, i.e. the new transform is
		/// <c>matrix * current</c>: positions are transformed by the given matrix first, then by the
		/// current transform.
		/// </summary>
		Multiply,
	}
}

/// <summary>
/// A change to the current transform, for <see cref="CanvasParamsOverride.Transform"/>.
/// </summary>
/// <remarks>
/// <para>
/// Matrices follow the <see cref="System.Numerics"/> convention of row vectors, so
/// <c>a * b</c> applies <c>a</c> first, then <c>b</c>. <see cref="Multiply(Matrix3x2)"/> makes the
/// new transform <c>matrix * current</c>, which nests the way a scene graph does: e.g. pushing a
/// camera transform, then multiplying in an object's transform, places the object in the camera's
/// space.
/// </para>
/// <para>
/// The matrix must be invertible (see <see cref="MatrixInvert"/>), and so must the resulting
/// transform; <see cref="Canvas.PushParams(in CanvasParamsOverride)"/> checks the latter.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid. Use <see cref="Set(Matrix3x2)"/> or
/// <see cref="Multiply(Matrix3x2)"/> instead.
/// </para>
/// </remarks>
public readonly struct CanvasTransformOverride : IEquatable<CanvasTransformOverride> {
	private readonly CanvasTransformOverrideKind kind;
	private readonly Matrix3x2 matrix;

	/// <summary>
	/// How <see cref="Matrix"/> is applied to the current transform.
	/// </summary>
	/// <exception cref="InvalidDefaultException">
	/// Thrown if this is the <see langword="default"/> value.
	/// </exception>
	public CanvasTransformOverrideKind Kind => IsDefault ? throw InvalidDefaultException.For<CanvasTransformOverride>() : kind;

	/// <summary>
	/// Matrix to set or multiply in. Always invertible.
	/// </summary>
	/// <exception cref="InvalidDefaultException">
	/// Thrown if this is the <see langword="default"/> value.
	/// </exception>
	public Matrix3x2 Matrix => IsDefault ? throw InvalidDefaultException.For<CanvasTransformOverride>() : matrix;

	// the factories reject the zero matrix, so only the default value has it
	internal bool IsDefault => matrix == default;

	private CanvasTransformOverride(CanvasTransformOverrideKind kind, Matrix3x2 matrix) {
		this.kind = kind;
		this.matrix = matrix;
	}

	/// <summary>
	/// Creates an override that replaces the current transform with <paramref name="matrix"/>.
	/// </summary>
	/// <param name="matrix">
	/// The new transform; must be invertible (see <see cref="MatrixInvert"/>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="matrix"/> isn't invertible.
	/// </exception>
	public static CanvasTransformOverride Set(Matrix3x2 matrix) {
		MatrixInvert.ThrowIfNotInvertible(matrix);
		return new CanvasTransformOverride(CanvasTransformOverrideKind.Set, matrix);
	}

	/// <summary>
	/// Creates an override that applies <paramref name="matrix"/> in the space of the current
	/// transform, making the new transform <c>matrix * current</c>.
	/// </summary>
	/// <param name="matrix">
	/// Matrix applied to positions before the current transform; must be
	/// invertible (see <see cref="MatrixInvert"/>).
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="matrix"/> isn't invertible.
	/// </exception>
	public static CanvasTransformOverride Multiply(Matrix3x2 matrix) {
		MatrixInvert.ThrowIfNotInvertible(matrix);
		return new CanvasTransformOverride(CanvasTransformOverrideKind.Multiply, matrix);
	}

	public bool Equals(CanvasTransformOverride other) => kind == other.kind && matrix.Equals(other.matrix);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasTransformOverride other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)kind.Tag, matrix);
	public static bool operator ==(CanvasTransformOverride left, CanvasTransformOverride right) => left.Equals(right);
	public static bool operator !=(CanvasTransformOverride left, CanvasTransformOverride right) => !left.Equals(right);
}

/// <summary>
/// How <see cref="Canvas"/> draws are written to the target. Currently, consists of the blend state
/// and color write mask.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CanvasOutputStates"/> has the common combinations.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid, but writes nothing, since its
/// <see cref="WriteMask"/> is <see cref="ColorWriteMask.None"/>.
/// </para>
/// </remarks>
public readonly record struct CanvasOutputState {
	/// <summary>
	/// How drawn colors are combined with the target's, or <see langword="null"/> to replace the
	/// target's colors.
	/// </summary>
	public required BlendState? Blend { get; init; }

	/// <summary>
	/// Which color channels of the target are written.
	/// </summary>
	public required ColorWriteMask WriteMask { get; init; }
}

/// <summary>
/// Common <see cref="CanvasOutputState"/> values.
/// </summary>
public static class CanvasOutputStates {
	/// <summary>
	/// No blending: drawn colors, including alpha, replace the target's.
	/// </summary>
	public static readonly CanvasOutputState Opaque = new() {
		Blend = null,
		WriteMask = ColorWriteMask.All,
	};

	/// <summary>
	/// Blending for colors with straight (non-premultiplied) alpha; see
	/// <see cref="BlendStates.Alpha"/>.
	/// </summary>
	public static readonly CanvasOutputState Alpha = new() {
		Blend = BlendStates.Alpha,
		WriteMask = ColorWriteMask.All,
	};

	/// <summary>
	/// Blending for colors with premultiplied alpha; see <see cref="BlendStates.PremulAlpha"/>.
	/// </summary>
	public static readonly CanvasOutputState PremulAlpha = new() {
		Blend = BlendStates.PremulAlpha,
		WriteMask = ColorWriteMask.All,
	};
}

/// <summary>
/// How <see cref="Canvas"/> renders textured draws.
/// </summary>
/// <remarks>
/// <para>
/// Untextured draws ignore the material. Obtain one from <see cref="Color"/>, <see cref="Rmask"/>,
/// or <see cref="Sdf(SdfParams)"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasMaterial : IEquatable<CanvasMaterial> {
	private readonly TextureInterpretation textureInterpretation;
	private readonly SdfParams? sdfParams;

	private CanvasMaterial(TextureInterpretation textureInterpretation, SdfParams? sdfParams) {
		this.textureInterpretation = textureInterpretation;
		this.sdfParams = sdfParams;
	}

	/// <summary>
	/// A material that interprets textures as ordinary color data.
	/// </summary>
	public static readonly CanvasMaterial Color = new(TextureInterpretation.Color, null);

	/// <summary>
	/// A material that interprets textures' red channels as coverage masks and ignores the other
	/// channels.
	/// </summary>
	/// <remarks>
	/// Pairs well with a red-only texture (e.g. <see cref="Texture2dFormat.R8Unorm"/>); the "red"
	/// part can then mentally be entirely ignored, treating the texture as if it's a grayscale image
	/// where each pixel is represented by its brightness.
	/// </remarks>
	public static readonly CanvasMaterial Rmask = new(TextureInterpretation.Rmask, null);

	/// <summary>
	/// Creates a material that interprets textures as signed distance fields.
	/// </summary>
	/// <param name="params">How to decode the distance field.</param>
	/// <exception cref="ArgumentInvalidDefaultException">
	/// Thrown if <paramref name="params"/> is the <see langword="default"/> value.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="params"/> breaks one of the constraints <see cref="SdfParams"/>
	/// documents.
	/// </exception>
	/// <remarks>
	/// Textured draws with this material require <see cref="TexFilter.Linear"/> sampling, and throw
	/// with any other filter, as sharper sampling defeats the point of an SDF. That's not the
	/// default, so you have to set it explicitly via <see cref="CanvasParams.Sampling"/> or
	/// <see cref="TexDraw.Sampling"/>.
	/// </remarks>
	public static CanvasMaterial Sdf(SdfParams @params) {
		if (@params == default)
			throw ArgumentInvalidDefaultException.For<SdfParams>(nameof(@params));
		if (!float.IsFinite(@params.DistanceRangeTexels) || @params.DistanceRangeTexels <= 0f)
			throw new ArgumentOutOfRangeException(nameof(@params), @params, "SdfParams.DistanceRangeTexels must be positive and finite");
		if (!(@params.EdgeValue >= 0f && @params.EdgeValue <= 1f))
			throw new ArgumentOutOfRangeException(nameof(@params), @params, "SdfParams.EdgeValue must be in [0, 1]");
		if (!float.IsFinite(@params.SoftnessPixels) || @params.SoftnessPixels < 0f)
			throw new ArgumentOutOfRangeException(nameof(@params), @params, "SdfParams.SoftnessPixels must be non-negative and finite");
		if (!float.IsFinite(@params.OutlineWidthPixels) || @params.OutlineWidthPixels < 0f)
			throw new ArgumentOutOfRangeException(nameof(@params), @params, "SdfParams.OutlineWidthPixels must be non-negative and finite");
		return new CanvasMaterial(TextureInterpretation.Sdf, @params);
	}

	/// <summary>
	/// How sampled texture data is interpreted.
	/// </summary>
	public TextureInterpretation TextureInterpretation =>
		IsDefault ? throw InvalidDefaultException.For<CanvasMaterial>() : textureInterpretation;

	/// <summary>
	/// Parameters for <see cref="TextureInterpretation.Sdf"/>, or <see langword="null"/> for the
	/// other interpretations.
	/// </summary>
	public SdfParams? SdfParams =>
		IsDefault ? throw InvalidDefaultException.For<CanvasMaterial>() : sdfParams;

	internal bool IsDefault => textureInterpretation == default;

	public bool Equals(CanvasMaterial other) => textureInterpretation == other.textureInterpretation && sdfParams == other.sdfParams;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasMaterial other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)textureInterpretation.Tag, sdfParams);
	public static bool operator ==(CanvasMaterial left, CanvasMaterial right) => left.Equals(right);
	public static bool operator !=(CanvasMaterial left, CanvasMaterial right) => !left.Equals(right);
}
