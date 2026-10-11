// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.DevAnalyzers.Attributes;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Target for a <see cref="Canvas"/> to draw into: either the frame's primary output, or an
/// offscreen <see cref="RenderTarget2d"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RenderTarget2d"/> converts to <see cref="CanvasTarget"/> implicitly.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Primary"/>.
/// </para>
/// </remarks>
public readonly struct CanvasTarget : IEquatable<CanvasTarget> {
	/// <summary>
	/// The frame's primary output; see <see cref="RenderFrame.PrimaryView"/>.
	/// </summary>
	public static readonly CanvasTarget Primary = default;

	/// <summary>
	/// Whether this is <see cref="Primary"/>.
	/// </summary>
	[MemberNotNullWhen(false, nameof(rt))]
	public bool IsPrimary => rt is null;

	/// <summary>
	/// The render target this refers to.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if this is <see cref="Primary"/>.
	/// </exception>
	public readonly RenderTarget2d RenderTarget => rt ?? throw new InvalidOperationException("primary target has no RenderTarget2d");
	private readonly RenderTarget2d? rt;

	/// <summary>
	/// Creates a target referring to an offscreen render target.
	/// </summary>
	/// <param name="renderTarget">Render target to draw into.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="renderTarget"/> is <see langword="null"/>.
	/// </exception>
	public CanvasTarget(RenderTarget2d renderTarget) {
		ArgumentNullException.ThrowIfNull(renderTarget);
		rt = renderTarget;
	}

	public bool Equals(CanvasTarget other) => ReferenceEquals(rt, other.rt);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasTarget other && Equals(other);
	public override int GetHashCode() => rt?.GetHashCode() ?? 0;
	public static bool operator ==(CanvasTarget left, CanvasTarget right) => left.Equals(right);
	public static bool operator !=(CanvasTarget left, CanvasTarget right) => !left.Equals(right);

	/// <summary>
	/// Creates a target referring to <paramref name="target"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="target"/> is <see langword="null"/>.
	/// </exception>
	public static implicit operator CanvasTarget(RenderTarget2d target) => new(target);
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

		/// <summary>
		/// The scissor is the intersection of the current one and the given rectangle. Only valid in a
		/// <see cref="CanvasParamsOverride"/>, not in the base <see cref="CanvasParams"/>.
		/// </summary>
		Intersect,
	}
}

/// <summary>
/// A scissor for <see cref="Canvas"/>, i.e. the rectangle of the target that draws are clipped to.
/// </summary>
/// <remarks>
/// <para>
/// Rectangles are in target pixels, and not affected by <see cref="CanvasParams.Transform"/>, since
/// the scissor is render pass state rather than geometry.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </para>
/// </remarks>
public readonly struct CanvasScissor : IEquatable<CanvasScissor> {
	/// <summary>
	/// How <see cref="Rect"/> is interpreted.
	/// </summary>
	public readonly CanvasScissorKind Kind;

	/// <summary>
	/// Scissor rectangle, in target pixels.
	/// </summary>
	/// <remarks>
	/// Ignored for <see cref="CanvasScissorKind.None"/>.
	/// </remarks>
	public readonly RectI Rect;

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
	/// <param name="rect">
	/// Scissor rectangle, in target pixels. Its width and height must not be negative.
	/// </param>
	public static CanvasScissor Set(RectI rect) => new(CanvasScissorKind.Set, rect);

	/// <summary>
	/// Creates a scissor that is the intersection of the current one and <paramref name="rect"/>.
	/// </summary>
	/// <param name="rect">
	/// Rectangle to intersect the current scissor with, in target pixels. Its width and height must
	/// not be negative.
	/// </param>
	/// <remarks>
	/// Only valid in a <see cref="CanvasParamsOverride"/>, not in the base <see cref="CanvasParams"/>.
	/// </remarks>
	public static CanvasScissor Intersect(RectI rect) => new(CanvasScissorKind.Intersect, rect);

	public bool Equals(CanvasScissor other) => Kind == other.Kind && Rect.Equals(other.Rect);
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasScissor other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)Kind.Tag, Rect);
	public static bool operator ==(CanvasScissor left, CanvasScissor right) => left.Equals(right);
	public static bool operator !=(CanvasScissor left, CanvasScissor right) => !left.Equals(right);
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
/// Untextured draws ignore the material. <see cref="CanvasMaterials"/> has the common values.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <see cref="TextureInterpretation"/>
/// is.
/// </para>
/// </remarks>
public readonly struct CanvasMaterial : IEquatable<CanvasMaterial> {
	/// <summary>
	/// How sampled texture data is interpreted.
	/// </summary>
	public required TextureInterpretation TextureInterpretation { get; init; }

	/// <summary>
	/// Parameters for <see cref="TextureInterpretation.Sdf"/>; required for it, and ignored
	/// otherwise.
	/// </summary>
	public SdfParams? SdfParams { get; init; }

	public bool Equals(CanvasMaterial other) => TextureInterpretation == other.TextureInterpretation && SdfParams == other.SdfParams;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is CanvasMaterial other && Equals(other);
	public override int GetHashCode() => HashCode.Combine((int)TextureInterpretation.Tag, SdfParams);
	public static bool operator ==(CanvasMaterial left, CanvasMaterial right) => left.Equals(right);
	public static bool operator !=(CanvasMaterial left, CanvasMaterial right) => !left.Equals(right);
}

/// <summary>
/// Common <see cref="CanvasMaterial"/> values.
/// </summary>
public static class CanvasMaterials {
	/// <summary>
	/// A material that interprets textures as ordinary color data.
	/// </summary>
	public static readonly CanvasMaterial Color = new() {
		TextureInterpretation = TextureInterpretation.Color,
	};

	/// <summary>
	/// A material that interprets textures' red channels as coverage masks and ignores the other
	/// channels.
	/// </summary>
	/// <remarks>
	/// Pairs well with a red-only texture (e.g. <see cref="Texture2dFormat.R8Unorm"/>); the "red"
	/// part can then entirely be ignored, treating the texture as if it's a grayscale image where
	/// each pixel is represented by its brightness.
	/// </remarks>
	public static readonly CanvasMaterial Rmask = new() {
		TextureInterpretation = TextureInterpretation.Rmask,
	};

	/// <summary>
	/// Creates a material that interprets textures as signed distance fields.
	/// </summary>
	/// <param name="params">How to decode the distance field.</param>
	public static CanvasMaterial Sdf(SdfParams @params) => new() {
		TextureInterpretation = TextureInterpretation.Sdf,
		SdfParams = @params,
	};
}
