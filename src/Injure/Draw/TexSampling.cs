// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// High-level texture filtering mode between texel centers. See <see cref="TexSampling"/> for when
/// to use which.
/// </summary>
/// <remarks>
/// <para>
/// Separate mag/min/mipmap filters are unsupported at the <see cref="Draw"/> layer, and require
/// raw <see cref="Gpu"/> usage.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Nearest"/>.
/// </para>
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // nearest is the plain "no filtering" choice, so it's an alright default
public readonly partial struct TexFilter {
	/// <summary>Raw switch tag for <see cref="TexFilter"/>.</summary>
	public enum Case {
		/// <summary>
		/// Uses the texel nearest to the sampled point, keeping hard texel edges. Multi-mip textures
		/// use the nearest mip level.
		/// </summary>
		Nearest,

		/// <summary>
		/// Interpolates bilinearly between the four texels around the sampled point. When shrinking a
		/// multi-mip texture, interpolates trilinearly instead: bilinearly within the two nearest mip
		/// levels, then linearly between those.
		/// </summary>
		Linear,

		/// <summary>
		/// At a scale above 1, colors each destination pixel by area: a pixel contained within one texel
		/// gets that texel's color, and a pixel that a texel edge passes through mixes the texels it
		/// overlaps, weighted by how much of the pixel each texel covers. At a scale of 1 or below,
		/// equivalent to <see cref="Linear"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Implemented with a shader rather than a GPU sampler filtering mode.
		/// </para>
		/// <para>
		/// The scale applies per texture axis, so a draw stretched along one axis and shrunk along the
		/// other is area-weighted along the first and linear along the second. Mip level selection is
		/// the same as <see cref="Linear"/>'s.
		/// </para>
		/// <para>
		/// Each texel edge passes through at most one row or column of pixels, so at a fractional scale,
		/// about 1/scale of the rows and 1/scale of the columns are mixes. At an integer scale, where
		/// texel edges are on pixel boundaries, there are no mixes, just like <see cref="Nearest"/>.
		/// </para>
		/// <para>
		/// Below a scale of 2, the mixed pixels of neighboring edges can be adjacent, so a texel can
		/// appear only in mixes, and the result may look closer to <see cref="TexFilter.Linear"/>.
		/// </para>
		/// <para>
		/// The weights are exact for draws whose texture axes align with the target's axes, i.e. ones
		/// with only translation, scaling, and mirroring. With rotation or shear, a pixel covers a
		/// parallelogram of texels, whose extent along each texture axis sets the scale along it, and
		/// the weights approximate its coverage. At 45 degrees, the weights are off by up to 1/8 of the
		/// difference between two texels along a texel edge, and by up to about 1/5 near a texel
		/// corner.
		/// </para>
		/// </remarks>
		AreaWeighted,
	}
}

/// <summary>
/// High-level texture addressing mode for UV coordinates outside of [0, 1]. See
/// <see cref="TexSampling"/> for when to use which; if you're not sure which one you want, it's
/// most likely <see cref="Clamp"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per-axis address modes are unsupported at the <see cref="Draw"/> layer, and require raw
/// <see cref="Gpu"/> usage.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Clamp"/>.
/// </para>
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // clamp is what a sprite drawn within [0, 1] expects
public readonly partial struct TexAddressing {
	/// <summary>Raw switch tag for <see cref="TexAddressing"/>.</summary>
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
/// Texture filter mode + address mode. The address mode is <see cref="TexAddressing.Clamp"/>
/// unless explicitly set, since that's what you want most of the time.
/// </summary>
/// <param name="Filter">How the texture is filtered between texel centers.</param>
/// <param name="Addressing">How texture UV coordinates outside of [0, 1] are handled.</param>
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
/// pixel edges is important; make sure to use an integer scale.
/// </para>
/// <para>
/// The second option, <see cref="TexFilter.Linear"/>, uses bilinear (or trilinear for multi-mip
/// textures) filtering when scaling. As such, both kinds of scales don't distort the image but
/// produce a somewhat more blurry result where pixel edges are mushed together. Use
/// <see cref="TexFilter.Linear"/> for things where the exact pixels don't matter as much as the
/// overall look, where a rotation / fractional scale / etc. is more natural: photos, painted art,
/// smooth UI, text, etc. <see cref="TextureInterpretation.Sdf"/> textures require
/// <see cref="TexFilter.Linear"/>, since other filters defeat the point of an SDF.
/// </para>
/// <para>
/// The third option, <see cref="TexFilter.AreaWeighted"/>, gives a destination pixel contained
/// within a texel that texel's color, and gives a pixel that a texel edge passes through a mix of
/// the texels it overlaps, weighted by how much of the pixel each texel covers (hence the name
/// "area-weighted"). If the scale is 1 or lower, it acts like plain <see cref="TexFilter.Linear"/>
/// instead. Pixels stay evenly sized at a fractional scale, and edges move smoothly with subpixel
/// movement rather than shimmering. Do note, though, that the result may look closer to
/// <see cref="TexFilter.Linear"/> below a scale of 2. Use <see cref="TexFilter.AreaWeighted"/> for
/// pixel art that can't stay at an integer scale, e.g. a low-resolution render target upscaled to
/// the full window size.
/// </para>
/// <para>
/// Linear filtering has two gotchas. The first is that without mips (see
/// <see cref="Texture2dParams.Mips"/>), shrinking a texture below about half its size still
/// aliases. The second is that, in an atlas, samples near a region's edge blend with the
/// neighboring region, unless regions are padded by repeating their edge texels; smaller mip levels
/// blend across a wider band, so they need more padding. <see cref="TexFilter.AreaWeighted"/> has
/// both gotchas too; when scaling up, it only blends the pixels on a region's edge, so one texel of
/// padding is enough.
/// </para>
/// <para>
/// Use <see cref="TexAddressing.Clamp"/> for ordinary sprites, and
/// <see cref="TexAddressing.Repeat"/> or <see cref="TexAddressing.MirrorRepeat"/> for tiling a
/// texture with a <see cref="TexRegion.Uv(RectF)"/> region that extends beyond [0, 1]. It applies
/// to the whole underlying texture, so it can't tile one <see cref="Texture2dSlice"/> of an atlas.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Nearest"/>.
/// </para>
/// </remarks>
public readonly record struct TexSampling(TexFilter Filter, TexAddressing Addressing) {
	/// <summary>
	/// <see cref="TexFilter.Nearest"/> + <see cref="TexAddressing.Clamp"/>.
	/// </summary>
	public static readonly TexSampling Nearest = default;

	/// <summary>
	/// <see cref="TexFilter.Linear"/> + <see cref="TexAddressing.Clamp"/>.
	/// </summary>
	public static readonly TexSampling Linear = new(TexFilter.Linear, TexAddressing.Clamp);

	/// <summary>
	/// <see cref="TexFilter.AreaWeighted"/> + <see cref="TexAddressing.Clamp"/>.
	/// </summary>
	public static readonly TexSampling AreaWeighted = new(TexFilter.AreaWeighted, TexAddressing.Clamp);

	private static readonly int wrapCount = TexAddressing.Enum.Tags.Length;

	// dense index over all values, for per-value caches; tags are 0-based and contiguous
	internal int Index => (int)Filter.Tag * wrapCount + (int)Addressing.Tag;
	internal static readonly int IndexCount = TexFilter.Enum.Tags.Length * wrapCount;
}
