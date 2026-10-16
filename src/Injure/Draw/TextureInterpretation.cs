// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// Describes how <see cref="Canvas"/> interprets sampled texture data.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct TextureInterpretation {
	/// <summary>Raw switch tag for <see cref="TextureInterpretation"/>.</summary>
	public enum Case {
		/// <summary>
		/// Interprets the texture as ordinary color data.
		/// </summary>
		Color = 1,

		/// <summary>
		/// Interprets the texture's red channel as a coverage mask, which scales the draw color's alpha.
		/// </summary>
		Rmask,

		/// <summary>
		/// Interprets the texture's red channel as a single-channel signed distance field.
		/// </summary>
		/// <remarks>
		/// Requires <see cref="SdfParams"/>; see <see cref="CanvasMaterial.Sdf(SdfParams)"/>. Drawing
		/// with any filter other than <see cref="TexFilter.Linear"/> throws.
		/// </remarks>
		Sdf,
	}
}

/// <summary>
/// How <see cref="TextureInterpretation.Sdf"/> decodes a signed distance field texture.
/// </summary>
/// <param name="DistanceRangeTexels">
/// The distance range the field was generated with, in texels. Must be positive and finite.
/// </param>
/// <param name="EdgeValue">The texel value at the shape's edge. Must be in [0, 1].</param>
/// <param name="SoftnessPixels">
/// Half the width of the antialiased transition at the edge, in target pixels. Must be
/// non-negative and finite.
/// </param>
/// <param name="OutlineWidthPixels">
/// Width of the outline around the shape, in target pixels; 0 for none. Must be non-negative and
/// finite.
/// </param>
/// <param name="OutlineColor">Color of the outline.</param>
/// <remarks>
/// <para>
/// <see cref="CanvasMaterial.Sdf(SdfParams)"/> checks the constraints on the parameters.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since a distance range of 0 can't describe a
/// distance field.
/// </para>
/// </remarks>
public readonly record struct SdfParams(
	float DistanceRangeTexels,
	float EdgeValue = 0.5f,
	float SoftnessPixels = 1.0f,
	float OutlineWidthPixels = 0.0f,
	SrgbColor32 OutlineColor = default
);
