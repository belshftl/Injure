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
/// offscreen <see cref="RenderTarget2d"/>), plus the <see cref="Draw.Viewport"/> that defines its
/// drawable area.
/// </summary>
/// <remarks>
/// <para>
/// Draws are transformed by <see cref="CanvasParams.Transform"/> into the drawable area, and
/// <see cref="Viewport"/> maps the drawable area to the target's pixels. The viewport belongs to
/// the target rather than to the transform, so
/// <see cref="CanvasTransformOverride.Set(Matrix3x2)"/> only replaces the transform, and the
/// viewport remains unchanged. Every draw is clipped to the drawable area. An example viewport for
/// the primary output would cover the whole target at the window's pixel density, making it so
/// that drawable units correspond to logical units rather than physical pixels.
/// </para>
/// <para>
/// <see cref="RenderTarget2d"/> converts to <see cref="CanvasTarget"/> implicitly, using
/// <see cref="Offscreen(RenderTarget2d)"/>, i.e. a viewport covering the whole render target at 1
/// pixel per unit, since needing any other viewport for an offscreen render target is very rare.
/// </para>
/// <para>
/// Equality compares both the render target and the viewport; see
/// <see cref="SameRenderTarget(CanvasTarget)"/> if you only care about the render target.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct CanvasTarget : IEquatable<CanvasTarget>, IHasInvalidDefault {
	private readonly RenderTarget2d? rt;
	private readonly bool isPrimary;
	private readonly Viewport viewport;

	/// <summary>
	/// The target's drawable area.
	/// </summary>
	public Viewport Viewport => IsDefault ? throw InvalidDefaultException.For<CanvasTarget>() : viewport;

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

	private CanvasTarget(RenderTarget2d? rt, bool isPrimary, Viewport viewport) {
		this.rt = rt;
		this.isPrimary = isPrimary;
		this.viewport = viewport;
	}

	/// <summary>
	/// Creates a target referring to the frame's primary output; see
	/// <see cref="RenderFrame.PrimaryView"/>.
	/// </summary>
	/// <param name="viewport">The drawable area of the primary output.</param>
	public static CanvasTarget Primary(Viewport viewport) {
		ArgumentInvalidDefaultException.ThrowIfDefault(viewport);
		return new CanvasTarget(null, true, viewport);
	}

	/// <summary>
	/// Creates a target referring to an offscreen render target, with a viewport covering all of it
	/// at 1 pixel per drawable unit.
	/// </summary>
	/// <param name="renderTarget">Render target to draw into.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="renderTarget"/> is <see langword="null"/>.
	/// </exception>
	public static CanvasTarget Offscreen(RenderTarget2d renderTarget) {
		ArgumentNullException.ThrowIfNull(renderTarget);
		return new CanvasTarget(renderTarget, false, Viewport.Whole(new Vector2(renderTarget.Width, renderTarget.Height)));
	}

	/// <summary>
	/// Creates a target referring to an offscreen render target.
	/// </summary>
	/// <param name="renderTarget">Render target to draw into.</param>
	/// <param name="viewport">The drawable area of the render target.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="renderTarget"/> is <see langword="null"/>.
	/// </exception>
	public static CanvasTarget Offscreen(RenderTarget2d renderTarget, Viewport viewport) {
		ArgumentNullException.ThrowIfNull(renderTarget);
		ArgumentInvalidDefaultException.ThrowIfDefault(viewport);
		return new CanvasTarget(renderTarget, false, viewport);
	}

	/// <summary>
	/// Whether this and <paramref name="other"/> refer to the same render target, or both to the
	/// primary output, regardless of their <see cref="Viewport"/>s.
	/// </summary>
	/// <remarks>
	/// Changing between targets for which this is <see langword="true"/> doesn't end the render
	/// pass; see <see cref="CanvasParams"/>.
	/// </remarks>
	public bool SameRenderTarget(CanvasTarget other) {
		ArgumentInvalidDefaultException.ThrowIfDefault(other);
		return IsPrimary == other.IsPrimary && ReferenceEquals(rt, other.rt);
	}

	public bool Equals(CanvasTarget other) => isPrimary == other.isPrimary && ReferenceEquals(rt, other.rt) && viewport.Equals(other.viewport);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasTarget other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(rt is null ? 0 : RuntimeHelpers.GetHashCode(rt), isPrimary, viewport);
	public static bool operator ==(CanvasTarget left, CanvasTarget right) => left.Equals(right);
	public static bool operator !=(CanvasTarget left, CanvasTarget right) => !left.Equals(right);

	/// <summary>
	/// Creates a target referring to <paramref name="target"/>; see
	/// <see cref="Offscreen(RenderTarget2d)"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="target"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator CanvasTarget(RenderTarget2d target) => Offscreen(target);

	internal bool IsDefault => rt is null && !isPrimary;
	bool IHasInvalidDefault.IsDefault => IsDefault;
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
	/// <param name="rect">Rectangle to map, e.g. in the coordinates draws are made in.</param>
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
	/// Scissors ignore <see cref="CanvasParams.Transform"/>, so a rectangle in draw coordinates needs
	/// to be mapped to the drawable area manually. Pass in the transform the scissor will apply
	/// under; if the same override also changes the transform, make sure to use the new transform,
	/// not the current <see cref="Canvas.CurrentTransform"/>. Negative scales (flips) and rotations
	/// by multiples of 90 degrees are allowed; the edges are reordered as needed.
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

/// <summary>
/// Describes how a <see cref="CanvasTransformOverride"/> is applied to the current transform.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct CanvasTransformOverrideKind {
	/// <summary>Raw switch tag for <see cref="CanvasTransformOverrideKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// Replaces the current transform with the given matrix.
		/// </summary>
		Set = 1,

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
public readonly struct CanvasTransformOverride : IEquatable<CanvasTransformOverride>, IHasInvalidDefault {
	private readonly CanvasTransformOverrideKind kind;
	private readonly Matrix3x2 matrix;

	/// <summary>
	/// How <see cref="Matrix"/> is applied to the current transform.
	/// </summary>
	public CanvasTransformOverrideKind Kind => IsDefault ? throw InvalidDefaultException.For<CanvasTransformOverride>() : kind;

	/// <summary>
	/// Matrix to set or multiply in. Always invertible.
	/// </summary>
	public Matrix3x2 Matrix => IsDefault ? throw InvalidDefaultException.For<CanvasTransformOverride>() : matrix;

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
	public override int GetHashCode() => HashCode.Combine(kind.RawTag, matrix);
	public static bool operator ==(CanvasTransformOverride left, CanvasTransformOverride right) => left.Equals(right);
	public static bool operator !=(CanvasTransformOverride left, CanvasTransformOverride right) => !left.Equals(right);

	internal bool IsDefault => kind == default;
	bool IHasInvalidDefault.IsDefault => IsDefault;
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
public readonly struct CanvasMaterial : IEquatable<CanvasMaterial>, IHasInvalidDefault {
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
		ArgumentInvalidDefaultException.ThrowIfDefault(@params, nameof(@params));
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

	public bool Equals(CanvasMaterial other) => textureInterpretation == other.textureInterpretation && sdfParams == other.sdfParams;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasMaterial other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(textureInterpretation.RawTag, sdfParams);
	public static bool operator ==(CanvasMaterial left, CanvasMaterial right) => left.Equals(right);
	public static bool operator !=(CanvasMaterial left, CanvasMaterial right) => !left.Equals(right);

	internal bool IsDefault => textureInterpretation == default;
	bool IHasInvalidDefault.IsDefault => IsDefault;
}
