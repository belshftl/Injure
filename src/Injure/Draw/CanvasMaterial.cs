// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.Gpu;

namespace Injure.Draw;

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
