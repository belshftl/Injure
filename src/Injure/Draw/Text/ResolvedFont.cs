// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;
using FreeTypeSharp;
using HarfBuzzSharp;
using Injure.Assets;
using Injure.DevAnalyzers.Attributes;
using static FreeTypeSharp.FT;
using static FreeTypeSharp.FT_LOAD;
using static FreeTypeSharp.FT_Render_Mode_;

namespace Injure.Draw.Text;

/// <summary>
/// How glyph coverage is rasterized.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Normal"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct FontRasterMode {
	/// <summary>Raw switch tag for <see cref="FontRasterMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Antialiased: each pixel's coverage is stored as a value between 0 and 1.</summary>
		Normal,

		/// <summary>
		/// Aliased: each pixel is either fully covered or not covered at all, e.g. for pixel fonts.
		/// </summary>
		Monochrome,
	}
}

/// <summary>
/// How glyph outlines are hinted (fitted to the pixel grid) before rasterization.
/// </summary>
/// <remarks>
/// <para>
/// Hinting only affects rasterization. Layout always uses the font's unhinted metrics, so hinting
/// never changes where glyphs are placed or how wide text is, and full hinting, which reshapes
/// glyphs horizontally to fit the pixel grid, isn't offered since the shapes wouldn't fit the
/// unhinted advances.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Light"/>.
/// </para>
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // light is the recommended setting, so it's the natural default
public readonly partial struct FontHinting {
	/// <summary>Raw switch tag for <see cref="FontHinting"/>.</summary>
	public enum Case {
		/// <summary>
		/// Fits outlines to the pixel grid vertically only (FreeType's light hinting), which sharpens
		/// horizontal stems, baselines, and x-heights without changing glyph widths.
		/// </summary>
		Light,

		/// <summary>
		/// Rasterizes outlines unchanged.
		/// </summary>
		None,
	}
}

/// <summary>
/// How a font face's glyphs are rasterized.
/// </summary>
/// <param name="Hinting">How outlines are fitted to the pixel grid.</param>
/// <param name="Mode">Whether coverage is antialiased.</param>
/// <param name="UseEmbeddedBitmaps">
/// Whether to use bitmaps embedded in the font for sizes it has them for, instead of rasterizing
/// the outlines.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is valid, and differs from <c>new FontRasterOptions()</c> in
/// that its <see cref="UseEmbeddedBitmaps"/> is <see langword="false"/>.
/// </remarks>
public readonly record struct FontRasterOptions(
	FontHinting Hinting = default,
	FontRasterMode Mode = default,
	bool UseEmbeddedBitmaps = true
) {
	/// <summary>
	/// Creates options with <see cref="FontHinting.Light"/> hinting,
	/// <see cref="FontRasterMode.Normal"/> rasterization, and embedded bitmaps enabled.
	/// </summary>
	public FontRasterOptions() : this(FontHinting.Light) {}
}

/// <summary>
/// Vertical metrics of a font, in em units, i.e. as fractions of the font size.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and has every metric at 0.
/// </remarks>
internal readonly record struct FontLineMetrics(
	float Ascent,
	float Descent,
	float LineGap
) {
	public float Height => Ascent + Descent + LineGap;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct ResolvedFontKey(
	FontSourceKind SourceKind,
	ulong Id,
	int FaceIndex
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// Identifies one <see cref="ResolvedFontState"/>, so caches keyed by it never mix up font
/// versions.
/// </summary>
/// <remarks>
/// <para>
/// The state purges its cache entries when it's disposed.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
internal readonly record struct FontCacheToken(
	ResolvedFontKey Key,
	ulong StateId
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// A font face resolved by a <see cref="TextSystem"/>, which has one per face.
/// </summary>
/// <remarks>
/// Not per size, since shaping and metrics don't depend on the size (layout is unhinted), and
/// rasterization takes the size and raster options per call.
/// </remarks>
internal interface IResolvedFont : IDisposable {
	/// <remarks>
	/// The returned state is only valid until the current thread's next asset reload boundary.
	/// </remarks>
	ResolvedFontState GetState();
	FontCacheToken GetCacheToken() => GetState().CacheToken;
}

internal sealed class ResolvedDirectFont(TextSystem owner, FontData source, int faceIndex) : IResolvedFont {
	private readonly TextSystem owner = owner;
	private readonly FontData source = source;
	private readonly int faceIndex = faceIndex;

	private ResolvedFontState? state;

	public ResolvedFontState GetState() =>
		state ??= new ResolvedFontState(owner, owner.GetOrCreateLoadedFace(source, faceIndex), new ResolvedFontKey(FontSourceKind.Direct, source.Id, faceIndex));

	public void Dispose() => state?.Dispose();
}

/// <remarks>
/// The face and state are derived values, so a reloaded font is picked up on the next
/// <see cref="GetState"/>, and replaced states are reclaimed at asset reload boundaries rather than
/// right away.
/// </remarks>
internal sealed class ResolvedAssetSourcedFont : IResolvedFont {
	private readonly DerivedAsset<ResolvedFontState> state;

	public ResolvedAssetSourcedFont(TextSystem owner, Asset<FontData> source, DerivedAsset<LoadedFontFace> face, int faceIndex) {
		ResolvedFontKey key = new(FontSourceKind.Asset, source.SlotId, faceIndex);
		state = DerivedAsset.Create(face, f => new ResolvedFontState(owner, f, key), $"font state of {source.AssetId} (face {faceIndex})");
	}

	public ResolvedFontState GetState() => state.Borrow().Value;

	public void Dispose() => state.Dispose();
}

/// <summary>
/// A face of a <see cref="FontData"/>, loaded into HarfBuzz.
/// </summary>
/// <remarks>
/// References the <see cref="FontData"/>'s bytes rather than copying them: a derived face version
/// pins the <see cref="FontData"/> version it was computed from, and a directly loaded face
/// references the <see cref="FontData"/>.
/// </remarks>
internal sealed unsafe class LoadedFontFace : IAssetStoreDisposable {
	private readonly byte[] data;
	private readonly GCHandle pinned;
	private AssetStoreCustody? custody;
	private bool disposed = false;

	public readonly int FaceIndex;
	public readonly string? DebugName;

	public readonly Blob HbBlob;
	public readonly Face HbFace;

	public int ByteLength => data.Length;
	public byte* DataPtr => (byte*)pinned.AddrOfPinnedObject();

	public LoadedFontFace(FontData source, int faceIndex) {
		// FontSpec rejects negative indices
		if (faceIndex < 0)
			throw new InternalStateException($"negative face index {faceIndex}");
		if (faceIndex >= source.FaceCount)
			throw new ArgumentOutOfRangeException(
				nameof(faceIndex),
				$"face index {faceIndex} is out of range for {source.DebugName ?? "this font"}; file contains {source.FaceCount} face(s)"
			);

		byte[] data = source.DataArr;
		GCHandle pinned = default;
		Blob? hbBlob = null;
		Face? hbFace = null;
		try {
			pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
			hbBlob = new Blob(pinned.AddrOfPinnedObject(), data.Length, MemoryMode.ReadOnly);
			hbFace = new Face(hbBlob, faceIndex);

			this.data = data;
			this.pinned = pinned;
			FaceIndex = faceIndex;
			DebugName = source.DebugName;
			HbBlob = hbBlob;
			HbFace = hbFace;

			pinned = default;
			hbBlob = null;
			hbFace = null;
		} finally {
			hbFace?.Dispose();
			hbBlob?.Dispose();
			if (pinned.IsAllocated)
				pinned.Free();
		}
	}

	public bool IsAssetStoreOwned => custody is { IsReleased: false };
	void IAssetStoreValue.AttachCustody(AssetStoreCustody custody) => this.custody = custody;

	public void Dispose() {
		custody?.ThrowIfHeld(this);
		if (disposed)
			return;
		disposed = true;
		HbFace.Dispose();
		HbBlob.Dispose();
		if (pinned.IsAllocated)
			pinned.Free();
	}
}

/// <remarks>
/// The <see langword="default"/> value is valid and is FreeType's default load flags and normal
/// render mode.
/// </remarks>
internal readonly record struct FontBackendOptions(
	FT_LOAD LoadFlags,
	FT_Render_Mode_ RenderMode
) {
	public static FontBackendOptions FromOptions(in FontRasterOptions opts) {
		FT_LOAD flags = FT_LOAD_DEFAULT;
		if (!opts.UseEmbeddedBitmaps)
			flags |= FT_LOAD_NO_BITMAP;
		flags |= opts.Hinting.Tag switch {
			FontHinting.Case.Light => (FT_LOAD)FT_LOAD_TARGET_LIGHT,
			FontHinting.Case.None => FT_LOAD_NO_HINTING,
			_ => throw new UnreachableException(),
		};
		// FT_LOAD_TARGET_MONO would hint horizontally too, so monochrome keeps the light target
		FT_Render_Mode_ renderMode = opts.Mode.Tag switch {
			FontRasterMode.Case.Normal => FT_RENDER_MODE_NORMAL,
			FontRasterMode.Case.Monochrome => FT_RENDER_MODE_MONO,
			_ => throw new UnreachableException(),
		};
		return new FontBackendOptions(flags, renderMode);
	}
}

/// <remarks>
/// The <see langword="default"/> value is valid and is an empty bitmap at the origin.
/// </remarks>
internal readonly record struct RasterizedGlyph(
	byte[] Pixels, // one byte of coverage per pixel, rows top to bottom, no padding
	int Width,
	int Height,
	int Left, // pixels from the origin to the bitmap's left edge, +X right
	int Top // pixels from the baseline to the bitmap's top edge, +Y up
);

internal sealed unsafe class ResolvedFontState : IAssetStoreDisposable {
	private static ulong nextStateId = 0;
	private readonly TextSystem owner;
	private readonly FtLibrary library;
	private readonly LoadedFontFace loadedFace;
	private AssetStoreCustody? custody;
	private int ftPixelSize = 0;
	private bool disposed = false;

	public readonly FontCacheToken CacheToken;

	public readonly FT_FaceRec_* FtFace;
	/// <summary>
	/// The HarfBuzz font that shapes the face.
	/// </summary>
	/// <remarks>
	/// Scaled to units per em, so positions come out in font units and divide into em units exactly.
	/// </remarks>
	public readonly Font HbFont;
	public readonly float UnitsPerEm;
	public readonly FontLineMetrics LineMetrics;

	public ResolvedFontState(TextSystem owner, LoadedFontFace loadedFace, ResolvedFontKey key) {
		CacheToken = new FontCacheToken(key, Interlocked.Increment(ref nextStateId));
		this.owner = owner;
		this.loadedFace = loadedFace;
		library = owner.Library;

		FT_FaceRec_* ftFace = null;
		Font? hbFont = null;
		library.AddRef();
		try {
			FtException.Check(FT_New_Memory_Face(library.Ptr, loadedFace.DataPtr, loadedFace.ByteLength, loadedFace.FaceIndex, &ftFace));
			int upem = ftFace->units_per_EM;
			// bitmap-only formats have no em square to measure unhinted metrics against
			if (upem == 0)
				throw new NotSupportedException($"{loadedFace.DebugName ?? "this font"} has no scalable outlines, which isn't supported");
			hbFont = new Font(loadedFace.HbFace);
			hbFont.SetFunctionsOpenType();
			hbFont.SetScale(upem, upem);

			float ascent = ftFace->ascender / (float)upem;
			float descent = -ftFace->descender / (float)upem;
			float height = ftFace->height / (float)upem;
			UnitsPerEm = upem;
			LineMetrics = new FontLineMetrics(ascent, descent, MathF.Max(0f, height - (ascent + descent)));
			FtFace = ftFace;
			HbFont = hbFont;

			ftFace = null;
			hbFont = null;
		} catch {
			library.Release();
			throw;
		} finally {
			hbFont?.Dispose();
			if (ftFace is not null)
				FT_Done_Face(ftFace);
		}
	}

	/// <summary>
	/// Rasterizes a glyph at <paramref name="pixelSize"/> pixels per em, with its origin shifted right
	/// by <paramref name="subpixelX"/> / 4 pixels. Returns <see langword="false"/> for glyphs without
	/// any pixels, e.g. spaces.
	/// </summary>
	public bool TryRasterize(uint glyphId, int pixelSize, in FontRasterOptions opts, int subpixelX, out RasterizedGlyph glyph) {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (pixelSize != ftPixelSize) {
			FtException.Check(FT_Set_Pixel_Sizes(FtFace, 0, (uint)pixelSize));
			ftPixelSize = pixelSize;
		}
		var backend = FontBackendOptions.FromOptions(in opts);
		// 26.6 fixed point, so a quarter pixel is 16
		FT_Vector_ delta = new() { x = subpixelX * 16, y = 0 };
		FT_Set_Transform(FtFace, null, &delta);
		FtException.Check(FT_Load_Glyph(FtFace, glyphId, backend.LoadFlags));
		FtException.Check(FT_Render_Glyph(FtFace->glyph, backend.RenderMode));
		FT_GlyphSlotRec_* slot = FtFace->glyph;
		int w = checked((int)slot->bitmap.width);
		int h = checked((int)slot->bitmap.rows);
		if (w == 0 || h == 0) {
			glyph = default;
			return false;
		}
		glyph = new RasterizedGlyph(readBitmap(slot->bitmap), w, h, slot->bitmap_left, slot->bitmap_top);
		return true;
	}

	private static byte[] readBitmap(FT_Bitmap_ bitmap) {
		int w = (int)bitmap.width;
		int h = (int)bitmap.rows;
		int absPitch = Math.Abs(bitmap.pitch);
		byte[] buf = new byte[checked(w * h)];
		if (bitmap.buffer is null)
			return buf;
		for (int y = 0; y < h; y++) {
			// negative pitch means rows are bottom-up
			byte* src = bitmap.pitch >= 0 ? bitmap.buffer + y * absPitch : bitmap.buffer + (h - 1 - y) * absPitch;
			Span<byte> dst = buf.AsSpan(y * w, w);
			switch (bitmap.pixel_mode) {
			case FT_Pixel_Mode_.FT_PIXEL_MODE_GRAY:
				new ReadOnlySpan<byte>(src, w).CopyTo(dst);
				break;
			case FT_Pixel_Mode_.FT_PIXEL_MODE_MONO:
				// one bit per pixel, most significant first
				for (int x = 0; x < w; x++)
					dst[x] = (src[x >> 3] & (0x80 >> (x & 0b111))) != 0 ? (byte)0xff : (byte)0x00;
				break;
			default:
				throw new NotSupportedException($"unsupported FreeType pixel mode {bitmap.pixel_mode}");
			}
		}
		return buf;
	}

	public bool IsAssetStoreOwned => custody is { IsReleased: false };
	void IAssetStoreValue.AttachCustody(AssetStoreCustody custody) => this.custody = custody;

	public void Dispose() {
		custody?.ThrowIfHeld(this);
		if (disposed)
			return;
		disposed = true;
		owner.ForgetFontState(CacheToken);
		HbFont.Dispose();
		FT_Done_Face(FtFace);
		library.Release();
	}
}
