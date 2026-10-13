// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// How a texture is filtered when sampled between texel centers; see <see cref="TexSampling"/> for
/// when to use which.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Nearest"/>.
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // nearest is the plain "no filtering" choice, so it's an alright default
public readonly partial struct TexFilter {
	/// <summary>Raw switch tag for <see cref="TexFilter"/>.</summary>
	public enum Case {
		/// <summary>
		/// Uses the texel nearest to the sampled point, keeping hard texel edges.
		/// </summary>
		Nearest,

		/// <summary>
		/// Interpolates linearly between the four texels around the sampled point.
		/// </summary>
		Linear,
	}
}

/// <summary>
/// How texture coordinates outside of [0, 1] map onto a texture; see <see cref="TexSampling"/> for
/// when to use which.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Clamp"/>.
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // clamp is what a sprite drawn within [0, 1] expects
public readonly partial struct TexWrap {
	/// <summary>Raw switch tag for <see cref="TexWrap"/>.</summary>
	public enum Case {
		/// <summary>
		/// Clamps coordinates to the edge, repeating the edge texels outwards.
		/// </summary>
		Clamp,

		/// <summary>
		/// Repeats the texture.
		/// </summary>
		Repeat,

		/// <summary>
		/// Repeats the texture, mirroring every other repetition.
		/// </summary>
		MirrorRepeat,
	}
}

/// <summary>
/// How <see cref="Canvas"/> samples a texture: the filter, and what happens outside of the texture.
/// </summary>
/// <param name="Filter">How the texture is filtered between texel centers.</param>
/// <param name="Wrap">How texture coordinates outside of [0, 1] map onto the texture.</param>
/// <remarks>
/// <para>
/// The primary thing sampler choice affects is how the texture is scaled. Since there's no such
/// thing as a fractional pixel, a fractional (i.e. non-integer) scale inherently has to be lossy
/// in one way or another. How an integer scale should be applied also has different approaches.
/// </para>
/// <para>
/// The default option, <see cref="TexFilter.Nearest"/>, uses nearest-neighbor scaling, which is
/// exact for integer scales, and produces unevenly-sized pixels at non-integer scales. Movement
/// usually also causes a "shimmer" from the uneven sizing from non-integer scale. Use
/// <see cref="TexFilter.Nearest"/> for pixel art and anything else where keeping existing sharp
/// pixel edges is important. Make sure to use an integer scale.
/// </para>
/// <para>
/// The other option, <see cref="TexFilter.Linear"/>, uses bilinear (or trilinear for multi-mip
/// textures) filtering when scaling. As such, both kinds of scales don't distort the image but
/// produce a somewhat more blurry result where pixel edges are mushed together. Use
/// <see cref="TexFilter.Linear"/> for things where the exact pixels don't matter as much as the
/// overall look, where a rotation / fractional scale / etc. is more natural: photos, painted art,
/// smooth UI, text, etc. <see cref="TextureInterpretation.Sdf"/> textures also fall into this
/// category, since non-linear sampling defeats the point of a signed distance field.
/// </para>
/// <para>
/// Linear filtering has two gotchas. The first is that without mips (see
/// <see cref="Texture2dCreateParams.Mips"/>), shrinking a texture below about half its size still
/// aliases. The second is that, in an atlas, samples near a region's edge blend with the
/// neighboring region, unless regions are padded by repeating their edge texels; smaller mip levels
/// blend across a wider band, so they need more padding.
/// </para>
/// <para>
/// Use <see cref="TexWrap.Clamp"/> for ordinary sprites, and <see cref="TexWrap.Repeat"/> or
/// <see cref="TexWrap.MirrorRepeat"/> for tiling a texture with a <see cref="TexRegion.Uv(RectF)"/>
/// region that extends beyond [0, 1]. Wrapping applies to the whole texture, so it can't tile one
/// <see cref="Texture2dSlice"/> of an atlas.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Nearest"/>.
/// </para>
/// </remarks>
public readonly record struct TexSampling(TexFilter Filter, TexWrap Wrap) {
	/// <summary>
	/// Nearest filtering, clamping to the edge.
	/// </summary>
	public static readonly TexSampling Nearest = default;

	/// <summary>
	/// Linear filtering, clamping to the edge.
	/// </summary>
	public static readonly TexSampling Linear = new(TexFilter.Linear, TexWrap.Clamp);

	private static readonly int wrapCount = TexWrap.Enum.Tags.Length;

	// dense index over all values, for per-value caches; tags are 0-based and contiguous
	internal int Index => (int)Filter.Tag * wrapCount + (int)Wrap.Tag;
	internal static readonly int IndexCount = TexFilter.Enum.Tags.Length * wrapCount;
}
