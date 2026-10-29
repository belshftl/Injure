// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Numerics;
using Injure.Primitives;

namespace Injure.Draw.Text;

/// <summary>
/// Draws <see cref="TextLayout"/>s, rasterizing glyphs for the effective scale.
/// </summary>
/// <remarks>
/// <para>
/// The raster size is the font size in target pixels along the longer axis of the effective
/// transform, rounded to the nearest whole pixel size, with midpoints rounded away from zero. The
/// glyph quad is stretched by what the rounding took away.
/// </para>
/// <para>
/// If the effective transform only scales and translates (no rotation/shear/mirroring), each
/// glyph's baseline is rounded to the nearest pixel row and its origin to the nearest quarter pixel,
/// both with midpoints rounded towards positive infinity, and glyphs are rasterized at the
/// quarter-pixel offset, so unstretched glyph bitmaps land exactly on target pixels while the
/// spacing stays within a quarter pixel of the layout's. Otherwise, glyphs are placed unsnapped.
/// </para>
/// </remarks>
internal static class TextRenderer {
	private const int subpixelSteps = 4;
	private static readonly ImmutableArray<uint> quadIndices = [0, 2, 1, 3, 1, 2];

	public static void Draw(OwnedCanvas oc, TextLayout layout, Vector2 at, SrgbStraight32 tint) {
		layout.RelayoutIfStale();
		ReadOnlySpan<LaidOutGlyph> glyphs = layout.Glyphs;
		if (glyphs.IsEmpty)
			return;
		ReadOnlySpan<ResolvedFontFallbackChain> chains = layout.Chains;
		GlyphAtlas atlas = layout.Owner.Atlas;
		int maxPixelSize = layout.Owner.MaxRasterPixelSize;

		Matrix3x2 m = oc.EffectiveTransform;
		float scale = MathF.Max(
			MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12),
			MathF.Sqrt(m.M21 * m.M21 + m.M22 * m.M22)
		);
		bool snap = m.M12 == 0f && m.M21 == 0f && m.M11 > 0f && m.M22 > 0f;

		using CanvasParamsScope scope = oc.PushParams(new CanvasParamsOverride(Material: CanvasMaterial.Rmask));
		foreach (LaidOutGlyph g in glyphs) {
			int pixelSize = Math.Clamp((int)MathF.Round(g.Size * scale, MidpointRounding.AwayFromZero), 1, maxPixelSize);
			float rasterPerUnit = pixelSize / g.Size;
			Vector2 pos = at + new Vector2(g.X, g.Y);
			ResolvedFontState state = chains[g.Chain].States[g.FontIndex];
			FontRasterOptions raster = chains[g.Chain].Raster[g.FontIndex];
			SrgbStraight32 color = tint == SrgbStraight32.White ? g.Color : multiply(g.Color, tint);
			GlyphAtlasEntry e;
			if (snap) {
				var px = Vector2.Transform(pos, m);
				float baseline = MathF.Floor(px.Y + 0.5f);
				float steps = MathF.Floor(px.X * subpixelSteps + 0.5f);
				float originX = MathF.Floor(steps / subpixelSteps);
				int subpixelX = (int)(steps - originX * subpixelSteps);
				if (!atlas.TryGetOrCreate(state, in raster, g.GlyphId, pixelSize, subpixelX, out e))
					continue;
				oc.PinGlyphPage(e.Page);
				// target pixels per raster pixel; 1 unless the raster size was rounded or clamped
				float stretchX = m.M11 / rasterPerUnit;
				float stretchY = m.M22 / rasterPerUnit;
				float x = originX + e.BitmapLeft * stretchX;
				float y = baseline - e.BitmapTop * stretchY;
				addQuad(oc, e, x, y, e.SrcPixels.Width * stretchX, e.SrcPixels.Height * stretchY, color, inPixels: true);
			} else {
				if (!atlas.TryGetOrCreate(state, in raster, g.GlyphId, pixelSize, 0, out e))
					continue;
				oc.PinGlyphPage(e.Page);
				float x = pos.X + e.BitmapLeft / rasterPerUnit;
				float y = pos.Y - e.BitmapTop / rasterPerUnit;
				addQuad(oc, e, x, y, e.SrcPixels.Width / rasterPerUnit, e.SrcPixels.Height / rasterPerUnit, color, inPixels: false);
			}
		}
	}

	// per channel, on the encoded values
	// x / 255 is never exactly a midpoint for an integer x, so adding 127 rounds to the nearest value
	private static SrgbStraight32 multiply(SrgbStraight32 a, SrgbStraight32 b) => new(
		(byte)((a.R * b.R + 127) / 255),
		(byte)((a.G * b.G + 127) / 255),
		(byte)((a.B * b.B + 127) / 255),
		(byte)((a.A * b.A + 127) / 255)
	);

	private static void addQuad(OwnedCanvas oc, in GlyphAtlasEntry e, float x, float y, float w, float h, SrgbStraight32 color, bool inPixels) {
		float invW = 1f / e.Page.Width, invH = 1f / e.Page.Height;
		float u0 = e.SrcPixels.X * invW, u1 = (e.SrcPixels.X + e.SrcPixels.Width) * invW;
		float v0 = e.SrcPixels.Y * invH, v1 = (e.SrcPixels.Y + e.SrcPixels.Height) * invH;
		ReadOnlySpan<Vertex2dTextureColor> verts = [
			new Vertex2dTextureColor(x, y, u0, v0, color),
			new Vertex2dTextureColor(x + w, y, u1, v0, color),
			new Vertex2dTextureColor(x, y + h, u0, v1, color),
			new Vertex2dTextureColor(x + w, y + h, u1, v1, color),
		];
		ResolvedDrawableTexture tex = new(e.Page.Texture);
		if (inPixels)
			oc.AddTexturedInPixels(in tex, TexSampling.Linear, verts, quadIndices.AsSpan());
		else
			oc.AddTextured(in tex, TexSampling.Linear, verts, quadIndices.AsSpan());
	}
}
