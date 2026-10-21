// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Draw.PixelConv;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Draw.Text;

/// <remarks>
/// Pinned by every frame that draws from it (see <see cref="GlyphPagePins"/>), so trimming doesn't
/// dispose a page that an unsubmitted frame samples.
/// </remarks>
internal sealed class GlyphAtlasPage : IDisposable {
	public required Texture2d Texture;
	public required int Width;
	public required int Height;

	public int WriteX;
	public int WriteY;
	public int RowHeight;

	public ulong LastUseStamp;
	public readonly HashSet<GlyphAtlasKey> Keys = new();

	private int refcount;
	public int Refcount => Volatile.Read(ref refcount);

	private bool disposed = false;

	public void Retain() {
		if (disposed)
			throw new InternalStateException("GlyphAtlasPage already disposed");
		if (Interlocked.Increment(ref refcount) <= 0)
			throw new InternalStateException("refcount overflow");
	}

	public void Release() {
		if (disposed)
			throw new InternalStateException("GlyphAtlasPage already disposed");
		if (Interlocked.Decrement(ref refcount) < 0)
			throw new InternalStateException("refcount went negative");
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		Texture.Dispose();
	}
}

/// <summary>
/// Set of pages a canvas drew glyphs from, registered into the <see cref="RenderFrame"/> to be
/// disposed on submit/discard, decrementing the refcounts.
/// </summary>
internal sealed class GlyphPagePins : IDisposable {
	private readonly HashSet<GlyphAtlasPage> pages = new();

	public void Pin(GlyphAtlasPage page) {
		if (pages.Add(page))
			page.Retain();
	}

	public void Dispose() {
		foreach (GlyphAtlasPage page in pages)
			page.Release();
		pages.Clear();
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct GlyphAtlasKey(
	FontCacheToken FontCacheToken,
	uint GlyphId,
	int PixelSize,
	FontRasterOptions Raster,
	int SubpixelX
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => FontCacheToken == default;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct GlyphAtlasEntry(
	GlyphAtlasPage Page,
	RectI SrcPixels,
	int BitmapLeft,
	int BitmapTop
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Page is null;
}

internal sealed class GlyphAtlas(GpuDevice gpuDevice, TextSystem text, int pageWidth = 1024, int pageHeight = 1024, int padding = 1, int maxPages = 16) : IDisposable {
	/// <summary>
	/// Size at which <see cref="empty"/> is cleared whole, since nothing else bounds it.
	/// </summary>
	private const int maxEmptyEntries = 4096;

	private readonly GpuDevice gpuDevice = gpuDevice;
	private readonly TextSystem text = text;
	private readonly Dictionary<GlyphAtlasKey, GlyphAtlasEntry> entries = new();
	/// <summary>
	/// Keys of glyphs without pixels, e.g. spaces, so they aren't loaded again on every draw.
	/// </summary>
	private readonly HashSet<GlyphAtlasKey> empty = new();
	private readonly List<GlyphAtlasPage> pages = new();

	private readonly int pageWidth = pageWidth;
	private readonly int pageHeight = pageHeight;
	private readonly int padding = padding;
	private readonly int maxPages = maxPages;

	private ulong nextUseStamp = 0; // first will be 1 since this gets incremented upfront
	private bool disposed = false;

	/// <summary>
	/// The atlas's pages; for tests.
	/// </summary>
	internal IReadOnlyList<GlyphAtlasPage> Pages => pages;

	private void clear() {
		for (int i = 0; i < pages.Count; i++)
			pages[i].Dispose();
		pages.Clear();
		entries.Clear();
		empty.Clear();
	}

	public void Clear() {
		if (disposed)
			throw new InternalStateException("GlyphAtlas already disposed");
		clear();
	}

	/// <summary>
	/// Gets the atlas entry of a glyph, rasterizing it and adding it to the atlas if necessary.
	/// </summary>
	/// <returns><see langword="false"/> for glyphs without pixels.</returns>
	public bool TryGetOrCreate(ResolvedFontState font, in FontRasterOptions raster, uint glyphId, int pixelSize, int subpixelX, out GlyphAtlasEntry entry) {
		if (disposed)
			throw new InternalStateException("GlyphAtlas already disposed");
		GlyphAtlasKey key = new(font.CacheToken, glyphId, pixelSize, raster, subpixelX);
		if (entries.TryGetValue(key, out entry)) {
			entry.Page.LastUseStamp = ++nextUseStamp;
			return true;
		}
		if (empty.Contains(key))
			return false;
		if (!font.TryRasterize(glyphId, pixelSize, in raster, subpixelX, out RasterizedGlyph glyph)) {
			if (empty.Count >= maxEmptyEntries)
				empty.Clear();
			empty.Add(key);
			return false;
		}
		GlyphAtlasPage page = alloc(glyph.Width, glyph.Height, out int x, out int y);
		page.Texture.Upload(x, y, glyph.Pixels, srcStride: glyph.Width, PixelFormat.R8_Unorm, glyph.Width, glyph.Height);
		entry = new GlyphAtlasEntry(page, new RectI(x, y, glyph.Width, glyph.Height), glyph.Left, glyph.Top);
		page.Keys.Add(key);
		page.LastUseStamp = ++nextUseStamp;
		entries.Add(key, entry);
		// the new glyph's page may be the only unpinned one, which trimming would otherwise free
		page.Retain();
		try {
			text.OnCacheActivity();
			Trim();
		} finally {
			page.Release();
		}
		return true;
	}

	private GlyphAtlasPage alloc(int width, int height, out int x, out int y) {
		int w = width + padding * 2;
		int h = height + padding * 2;
		int px, py;
		for (int i = 0; i < pages.Count; i++) {
			if (tryAllocOnPage(pages[i], w, h, out px, out py)) {
				x = px + padding;
				y = py + padding;
				return pages[i];
			}
		}
		GlyphAtlasPage page = newPage();
		if (!tryAllocOnPage(page, w, h, out px, out py))
			throw new InvalidOperationException($"glyph has a size of {width}x{height}, which is too large to fit into a {pageWidth}x{pageHeight} atlas page");
		x = px + padding;
		y = py + padding;
		return page;
	}

	private GlyphAtlasPage newPage() {
		GlyphAtlasPage page = new() {
			Texture = new Texture2d(
				gpuDevice,
				new Texture2dParams(
					Width: (uint)pageWidth,
					Height: (uint)pageHeight,
					Format: Texture2dFormat.R8Unorm
				)
			),
			Width = pageWidth,
			Height = pageHeight,
			WriteX = 0,
			WriteY = 0,
			RowHeight = 0,
		};
		pages.Add(page);
		return page;
	}

	private static bool tryAllocOnPage(GlyphAtlasPage page, int width, int height, out int x, out int y) {
		if (page.WriteX + width > page.Width) {
			page.WriteX = 0;
			page.WriteY += page.RowHeight;
			page.RowHeight = 0;
		}
		if (page.WriteY + height > page.Height || width > page.Width) {
			x = 0;
			y = 0;
			return false;
		}
		x = page.WriteX;
		y = page.WriteY;
		page.WriteX += width;
		page.RowHeight = Math.Max(page.RowHeight, height);
		return true;
	}

	/// <summary>
	/// Removes the entries of a font state's glyphs.
	/// </summary>
	/// <remarks>
	/// The glyphs stay on their pages until the page is freed, since frames may still sample them.
	/// </remarks>
	public void Forget(FontCacheToken token) {
		if (disposed)
			throw new InternalStateException("GlyphAtlas already disposed");
		foreach (GlyphAtlasKey key in entries.Keys.Where(k => k.FontCacheToken == token).ToArray()) {
			entries[key].Page.Keys.Remove(key);
			entries.Remove(key);
		}
		empty.RemoveWhere(k => k.FontCacheToken == token);
	}

	/// <summary>
	/// Frees the least recently used pages past <see cref="maxPages"/>.
	/// </summary>
	/// <remarks>
	/// Pages pinned by unfinished frames are skipped, so a single frame drawing more glyphs than
	/// <see cref="maxPages"/> pages hold grows the atlas past it until the frame is done.
	/// </remarks>
	public void Trim() {
		if (pages.Count <= maxPages)
			return;
		foreach (
			GlyphAtlasPage page in pages
				.Where(static p => p.Refcount == 0)
				.OrderBy(static p => p.LastUseStamp)
				.Take(pages.Count - maxPages)
				.ToArray()
		) {
			foreach (GlyphAtlasKey key in page.Keys)
				entries.Remove(key);
			page.Keys.Clear();
			pages.Remove(page);
			page.Dispose();
		}
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		clear();
	}
}
