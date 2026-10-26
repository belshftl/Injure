// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using FreeTypeSharp;
using Injure.Assets;
using Injure.Gpu;
using static FreeTypeSharp.FT;

namespace Injure.Draw.Text;

/// <summary>
/// Limits of a <see cref="TextSystem"/>'s caches.
/// </summary>
public sealed class TextCacheOptions {
	/// <summary>
	/// Maximum number of shaped text runs kept.
	/// </summary>
	public int MaxShapeEntries { get; init; } = 4096;

	/// <summary>
	/// Maximum estimated memory use of shaped text runs, in bytes.
	/// </summary>
	public int MaxShapeEstimatedCost { get; init; } = 8 * 1024 * 1024;

	/// <summary>
	/// Maximum number of remembered font fallback choices.
	/// </summary>
	public int MaxFallbackProbeEntries { get; init; } = 4096;

	/// <summary>
	/// Maximum estimated memory use of remembered font fallback choices, in bytes.
	/// </summary>
	public int MaxFallbackProbeEstimatedCost { get; init; } = 1 * 1024 * 1024;

	/// <summary>
	/// Width of a glyph atlas page, in pixels.
	/// </summary>
	public int GlyphAtlasPageWidth { get; init; } = 1024;

	/// <summary>
	/// Height of a glyph atlas page, in pixels.
	/// </summary>
	public int GlyphAtlasPageHeight { get; init; } = 1024;

	/// <summary>
	/// Empty pixels kept around each glyph in the atlas, so sampling doesn't bleed.
	/// </summary>
	public int GlyphAtlasPadding { get; init; } = 1;

	/// <summary>
	/// Number of glyph atlas pages beyond which the least recently used ones are freed. A frame
	/// that draws more glyphs than fit grows the atlas past it until the frame is done.
	/// </summary>
	public int MaxGlyphAtlasPages { get; init; } = 16;

	/// <summary>
	/// Largest font size glyphs are rasterized at, in pixels; text drawn larger is stretched.
	/// </summary>
	public int MaxRasterPixelSize { get; init; } = 256;

	/// <summary>
	/// Number of frames a layout made by
	/// <see cref="Canvas.Text(ReadOnlySpan{char}, in TextStyle, System.Numerics.Vector2, TextLayoutOptions?, Primitives.SrgbColor32?)"/>
	/// is kept for after its last use.
	/// </summary>
	public int LayoutCacheFrames { get; init; } = 120;

	/// <summary>
	/// Number of cache insertions between trims of all caches.
	/// </summary>
	public int TrimEveryNOperations { get; init; } = 128;
}

/// <summary>
/// A reference-counted FreeType library handle.
/// </summary>
/// <remarks>
/// Font states hold a reference, since the asset store may reclaim them after the
/// <see cref="TextSystem"/> is disposed.
/// </remarks>
internal sealed unsafe class FtLibrary {
	private int refs = 1;

	public FT_LibraryRec_* Ptr { get; }

	public FtLibrary() {
		FT_LibraryRec_* l;
		FtException.Check(FT_Init_FreeType(&l));
		Ptr = l;
	}

	public void AddRef() {
		if (Interlocked.Increment(ref refs) <= 1)
			throw new InternalStateException("FreeType library referenced after it was freed");
	}

	public void Release() {
		int n = Interlocked.Decrement(ref refs);
		if (n < 0)
			throw new InternalStateException("FreeType library reference count went negative");
		if (n == 0)
			FT_Done_FreeType(Ptr);
	}
}

/// <summary>
/// Loads fonts and lays out text.
/// </summary>
/// <remarks>
/// <para>
/// Text is laid out with <see cref="Layout(RichText, TextLayoutOptions?)"/> and drawn
/// with <see cref="Canvas.Text(TextLayout, System.Numerics.Vector2, Primitives.SrgbColor32?)"/>.
/// Alternatively, <see cref="Canvas"/> has integration with this type to do both at once as
/// <see cref="Canvas.Text(ReadOnlySpan{char}, in TextStyle, System.Numerics.Vector2, TextLayoutOptions?, Primitives.SrgbColor32?)"/>,
/// which keeps the layouts it makes in a cache inside this <see cref="TextSystem"/>, dropping
/// entries that haven't been drawn for a while (see <see cref="TextCacheOptions.LayoutCacheFrames"/>).
/// </para>
/// <para>
/// Text is split into paragraphs at <c>'\n'</c>, <c>'\r'</c>, <c>"\r\n"</c>, U+0085 NEXT LINE, and
/// U+2029 PARAGRAPH SEPARATOR, and into lines within a paragraph at U+2028 LINE SEPARATOR,
/// <c>'\v'</c>, and <c>'\f'</c>. Each paragraph has its own direction; see
/// <see cref="TextHorizontalAlign"/>. Separators aren't drawn, and text after one starts a new
/// line even if said line is empty, so e.g. <c>"a\n"</c> lays out to two lines.
/// </para>
/// <para>
/// Current limitations that exist simply due to something being unimplemented, rather than an
/// intentional design decision:
/// <list type="bullet">
/// <item><description>
/// Only horizontal text is supported, so e.g. vertical CJK layouts are impossible, and scripts
/// that require vertical writing, such as traditional Mongolian and Manchu script, are laid out
/// horizontally.
/// </description></item>
/// <item><description>
/// There is no automatic hyphenation. Manual hyphenation is supported (lines can break at U+00AD
/// SOFT HYPHEN, which draws a hyphen there; see <see cref="TextWrapMode.Greedy"/>), but the drawn
/// hyphen is always U+2010 HYPHEN or <c>'-'</c>, even in scripts that use a different character
/// (e.g. Armenian or Hebrew) or don't hyphenate words at all.
/// </description></item>
/// <item><description>
/// <c>'\t'</c> has no tab stops, and is drawn with whatever glyph the font maps it to, or the
/// missing-glyph glyph.
/// </description></item>
/// <item><description>
/// Each paragraph's base direction comes from its first strong character (left-to-right if it
/// has none), and there's no way to set it explicitly.
/// </description></item>
/// <item><description>
/// OpenType features (e.g. small caps or tabular figures) can't be selected; shaping uses
/// HarfBuzz's defaults for the script and language.
/// </description></item>
/// <item><description>
/// Variable fonts are always used at their default instance.
/// </description></item>
/// <item><description>
/// Color glyphs, e.g. color emoji, are unsupported.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Deliberate limitations:
/// <list type="bullet">
/// <item><description>
/// Fonts fall back only within a <see cref="FontFallbackChain"/>; system fonts are never used.
/// </description></item>
/// <item><description>
/// There's no synthetic bold/italic.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Not thread-safe, except for <see cref="LoadFont(byte[], string?)"/>.
/// </para>
/// </remarks>
public sealed unsafe class TextSystem : IDisposable {
	private sealed class CachedLayout {
		public required TextLayout Layout;
		public required ulong LastUsedFrame;
	}

	private readonly FtLibrary library = new();
	private readonly Lock probeLock = new();
	private readonly FtLibrary probeLibrary = new();
	private readonly Dictionary<ResolvedFontKey, IResolvedFont> fonts = new();
	private readonly Dictionary<(ulong FontId, int FaceIndex), LoadedFontFace> directFaces = new();
	private readonly Dictionary<(ulong SlotId, int FaceIndex), DerivedAsset<LoadedFontFace>> assetFaces = new();

	private readonly ITextItemizer itemizer;
	private readonly TextCacheOptions cacheOptions;
	private readonly ShapeCache shapeCache;
	private readonly FallbackProbeCache fallbackProbeCache;
	private readonly FallbackResolver fallbackResolver;
	private readonly GlyphAtlas atlas;
	private int opCounter = 0;
	private bool disposed = false;

	/// <summary>
	/// Layouts made by
	/// <see cref="Canvas.Text(ReadOnlySpan{char}, in TextStyle, System.Numerics.Vector2, TextLayoutOptions?, Primitives.SrgbColor32?)"/>,
	/// by text, then by fonts and style.
	/// </summary>
	private readonly Dictionary<string, List<CachedLayout>> layoutCache = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<CachedLayout>>.AlternateLookup<ReadOnlySpan<char>> layoutLookup;
	private readonly List<string> sweptTexts = new();
	private RenderFrame? lastFrame;
	private ulong frameNumber = 0;

	internal FtLibrary Library {
		get {
			ThrowIfDisposed();
			return library;
		}
	}

	internal GlyphAtlas Atlas {
		get {
			ThrowIfDisposed();
			return atlas;
		}
	}

	internal int MaxRasterPixelSize => cacheOptions.MaxRasterPixelSize;

	internal TextSystem(GpuDevice gpuDevice, ITextItemizer? itemizer = null, TextCacheOptions? cacheOptions = null) {
		this.itemizer = itemizer ?? DefaultTextItemizer.Instance;
		this.cacheOptions = cacheOptions ?? new TextCacheOptions();
		shapeCache = new ShapeCache(this, this.cacheOptions.MaxShapeEntries, this.cacheOptions.MaxShapeEstimatedCost);
		fallbackProbeCache = new FallbackProbeCache(this, this.cacheOptions.MaxFallbackProbeEntries, this.cacheOptions.MaxFallbackProbeEstimatedCost);
		fallbackResolver = new FallbackResolver(shapeCache, fallbackProbeCache);
		atlas = new GlyphAtlas(
			gpuDevice,
			this,
			this.cacheOptions.GlyphAtlasPageWidth,
			this.cacheOptions.GlyphAtlasPageHeight,
			this.cacheOptions.GlyphAtlasPadding,
			this.cacheOptions.MaxGlyphAtlasPages
		);
		layoutLookup = layoutCache.GetAlternateLookup<ReadOnlySpan<char>>();
	}

	internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

	// ==========================================================================
	// fonts
	private IResolvedFont resolveFont(FontData font, int faceIndex) {
		ResolvedFontKey key = new(FontSourceKind.Direct, font.Id, faceIndex);
		if (!fonts.TryGetValue(key, out IResolvedFont? fnt)) {
			fnt = new ResolvedDirectFont(this, font, faceIndex);
			fonts.Add(key, fnt);
		}
		return fnt;
	}

	private IResolvedFont resolveFont(Asset<FontData> font, int faceIndex) {
		ResolvedFontKey key = new(FontSourceKind.Asset, font.SlotId, faceIndex);
		if (!fonts.TryGetValue(key, out IResolvedFont? fnt)) {
			if (!assetFaces.TryGetValue((font.SlotId, faceIndex), out DerivedAsset<LoadedFontFace>? face)) {
				face = DerivedAsset.Create(font, f => new LoadedFontFace(f, faceIndex), $"font face {faceIndex} of {font.AssetId}");
				assetFaces.Add((font.SlotId, faceIndex), face);
			}
			fnt = new ResolvedAssetSourcedFont(this, font, face, faceIndex);
			fonts.Add(key, fnt);
		}
		return fnt;
	}

	private IResolvedFont resolveFont(in FontSpec spec) => spec.SourceKind switch {
		FontSourceKind.Direct => resolveFont(spec.Direct, spec.FaceIndex),
		FontSourceKind.Asset => resolveFont(spec.Asset, spec.FaceIndex),
		_ => throw new UnreachableException(),
	};

	internal ResolvedFontFallbackChain ResolveFallbackChain(FontFallbackChain chain) {
		ThrowIfDisposed();
		ReadOnlySpan<FontSpec> specs = chain.AllFonts;
		var resolved = new IResolvedFont[specs.Length];
		var raster = new FontRasterOptions[specs.Length];
		for (int i = 0; i < specs.Length; i++) {
			resolved[i] = resolveFont(in specs[i]);
			raster[i] = specs[i].Raster;
		}
		return new ResolvedFontFallbackChain(resolved, raster);
	}

	internal LoadedFontFace GetOrCreateLoadedFace(FontData font, int faceIndex) {
		ThrowIfDisposed();
		if (!directFaces.TryGetValue((font.Id, faceIndex), out LoadedFontFace? face)) {
			face = new LoadedFontFace(font, faceIndex);
			directFaces.Add((font.Id, faceIndex), face);
		}
		return face;
	}

	/// <summary>
	/// Removes a font state's cache entries.
	/// </summary>
	/// <remarks>
	/// Called when the state is disposed, so the caches don't keep entries nothing can hit anymore.
	/// </remarks>
	internal void ForgetFontState(FontCacheToken token) {
		if (disposed)
			return;
		shapeCache.Forget(token);
		atlas.Forget(token);
	}

	/// <summary>
	/// Loads a font from the contents of an OpenType font file or collection (see
	/// <see cref="FontData"/> for the supported formats), trusting that it's valid font data.
	/// </summary>
	/// <param name="data">The file's contents; copied out by this method.</param>
	/// <param name="debugName">Name used in error messages, e.g. the file name.</param>
	/// <returns>The loaded font.</returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="data"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="data"/> doesn't start with the header of an OpenType font or
	/// collection.
	/// </exception>
	/// <exception cref="FtException">
	/// Thrown if FreeType can't read <paramref name="data"/> despite its header.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this text system has been disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// A cheap check of <paramref name="data"/>'s first 4 bytes is done, checking that it has the
	/// header of an OpenType font or collection. FreeType then reads the table directory to count
	/// the faces, but invalid data past that can still only fail once the font is actually used.
	/// </para>
	/// <para>
	/// Thread-safe. The returned font can be used with any <see cref="TextSystem"/>.
	/// </para>
	/// </remarks>
	public FontData LoadFont(byte[] data, string? debugName = null) {
		ArgumentNullException.ThrowIfNull(data);
		FontData.ThrowIfNoOpentypeHeader(data, debugName);
		int faceCount;
		lock (probeLock) {
			// checked under the lock, which Dispose takes to free the probe library
			ThrowIfDisposed();
			fixed (byte* p = data) {
				FT_FaceRec_* probe = null;
				FtException.Check(FT_New_Memory_Face(probeLibrary.Ptr, p, data.Length, -1, &probe));
				try {
					faceCount = checked((int)probe->num_faces);
				} finally {
					if (probe is not null)
						FT_Done_Face(probe);
				}
			}
		}
		return new FontData(data.ToArray(), debugName, faceCount);
	}

	// ==========================================================================
	// layout
	internal LayoutResult RunLayouter(ResolvedFontFallbackChain[] chains, RichText text, int[] spanChains, in TextLayoutOptions options) =>
		TextLayouter.Layout(itemizer, fallbackResolver, shapeCache, chains, text, spanChains, in options);

	/// <summary>
	/// Lays out rich text.
	/// </summary>
	/// <param name="text">The text and its styles.</param>
	/// <param name="options">
	/// Line-breaking, alignment, and truncation options, or <see langword="null"/> for
	/// <see cref="TextLayoutOptions.Default"/>.
	/// </param>
	/// <returns>The layout.</returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="text"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="options"/>'s <see cref="TextLayoutOptions.MaxWidth"/> or
	/// <see cref="TextLayoutOptions.MaxHeight"/> is negative or NaN, or its
	/// <see cref="TextLayoutOptions.MaxLines"/> is less than 1.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this text system has been disposed.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if one of the text's fonts is an asset and the calling thread isn't attached to its
	/// asset store.
	/// </exception>
	public TextLayout Layout(RichText text, TextLayoutOptions? options = null) {
		ArgumentNullException.ThrowIfNull(text);
		TextLayoutOptions opts = options ?? TextLayoutOptions.Default;
		TextLayoutOptions.Validate(in opts, nameof(options));
		ThrowIfDisposed();
		return new TextLayout(this, text, in opts);
	}

	/// <summary>
	/// Lays out text with a single style.
	/// </summary>
	/// <param name="text">
	/// The text. Paragraphs and lines are separated as described on <see cref="TextSystem"/>. Unpaired
	/// surrogates are replaced with U+FFFD REPLACEMENT CHARACTER.
	/// </param>
	/// <param name="style">The style of the whole text.</param>
	/// <param name="options">
	/// Line-breaking, alignment, and truncation options, or <see langword="null"/> for
	/// <see cref="TextLayoutOptions.Default"/>.
	/// </param>
	/// <returns>The layout, whose <see cref="TextLayout.Text"/> has a single span.</returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="text"/> or <paramref name="style"/>'s
	/// <see cref="TextStyle.Fonts"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="style"/>'s <see cref="TextStyle.Size"/> isn't positive and finite,
	/// or <paramref name="options"/>'s <see cref="TextLayoutOptions.MaxWidth"/> or
	/// <see cref="TextLayoutOptions.MaxHeight"/> is negative or NaN, or its
	/// <see cref="TextLayoutOptions.MaxLines"/> is less than 1.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this text system has been disposed.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if one of <paramref name="style"/>'s fonts is an asset and the calling thread isn't
	/// attached to its asset store.
	/// </exception>
	public TextLayout Layout(string text, in TextStyle style, TextLayoutOptions? options = null) =>
		Layout(new RichText(text, in style), options);

	// ==========================================================================
	// layout cache

	// the layouts are keyed by text first; the entries for one text are few, so comparing their
	// styles and options linearly is cheaper than hashing a composite key without allocating it
	internal TextLayout GetCachedLayout(ReadOnlySpan<char> text, in TextStyle style, in TextLayoutOptions options) {
		ThrowIfDisposed();
		List<CachedLayout> list = cacheListFor(text, out string key);
		foreach (CachedLayout c in list) {
			ReadOnlySpan<RichTextSpan> spans = c.Layout.Text.SpansSpan;
			if (spans.Length == 1 && spans[0].Style == style && c.Layout.Options == options) {
				c.LastUsedFrame = frameNumber;
				return c.Layout;
			}
		}
		TextLayout layout = new(this, new RichText(key, in style), in options);
		list.Add(new CachedLayout { Layout = layout, LastUsedFrame = frameNumber });
		return layout;
	}

	internal TextLayout GetCachedLayout(RichText text, in TextLayoutOptions options) {
		ThrowIfDisposed();
		List<CachedLayout> list = cacheListFor(text.Text, out _);
		foreach (CachedLayout c in list) {
			if (c.Layout.Text.Equals(text) && c.Layout.Options == options) {
				c.LastUsedFrame = frameNumber;
				return c.Layout;
			}
		}
		TextLayout layout = new(this, text, in options);
		list.Add(new CachedLayout { Layout = layout, LastUsedFrame = frameNumber });
		return layout;
	}

	private List<CachedLayout> cacheListFor(ReadOnlySpan<char> text, out string key) {
		if (layoutLookup.TryGetValue(text, out string? existing, out List<CachedLayout>? list)) {
			key = existing;
			return list;
		}
		key = new string(text);
		list = new List<CachedLayout>(1);
		layoutCache.Add(key, list);
		return list;
	}

	/// <summary>
	/// Called when a canvas with this text system is created for <paramref name="frame"/>.
	/// </summary>
	/// <remarks>
	/// The first canvas created for a new frame starts the next frame of the layout cache.
	/// </remarks>
	internal void OnCanvasCreated(RenderFrame frame) {
		ThrowIfDisposed();
		if (ReferenceEquals(frame, lastFrame))
			return;
		lastFrame = frame;
		frameNumber++;
		sweepLayouts();
	}

	private void sweepLayouts() {
		ulong keep = (ulong)cacheOptions.LayoutCacheFrames;
		foreach ((string text, List<CachedLayout> list) in layoutCache) {
			list.RemoveAll(c => frameNumber - c.LastUsedFrame > keep);
			if (list.Count == 0)
				sweptTexts.Add(text);
		}
		foreach (string text in sweptTexts)
			layoutCache.Remove(text);
		sweptTexts.Clear();
	}

	// ==========================================================================
	// caches
	internal void OnCacheActivity() {
		ThrowIfDisposed();
		if (++opCounter < cacheOptions.TrimEveryNOperations)
			return;
		opCounter = 0;
		TrimCache();
	}

	/// <summary>
	/// Empties all caches, including the glyph atlas, except for atlas pages that unsubmitted frames
	/// draw from.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this text system has been disposed.
	/// </exception>
	public void ClearCache() {
		ThrowIfDisposed();
		shapeCache.Clear();
		fallbackProbeCache.Clear();
		atlas.Clear();
		layoutCache.Clear();
	}

	/// <summary>
	/// Shrinks the caches to their limits (see <see cref="TextCacheOptions"/>). Happens
	/// automatically every so often.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this text system has been disposed.
	/// </exception>
	public void TrimCache() {
		ThrowIfDisposed();
		shapeCache.Trim();
		fallbackProbeCache.Trim();
		atlas.Trim();
	}

	/// <summary>
	/// Frees the text system's font states, caches, and glyph atlas. Layouts made by it can't be
	/// drawn afterwards.
	/// </summary>
	/// <remarks>
	/// States of asset fonts are handed back to the asset store, which may reclaim them after this.
	/// </remarks>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		foreach (IResolvedFont f in fonts.Values)
			f.Dispose();
		fonts.Clear();
		foreach (DerivedAsset<LoadedFontFace> f in assetFaces.Values)
			f.Dispose();
		assetFaces.Clear();
		foreach (LoadedFontFace f in directFaces.Values)
			f.Dispose();
		directFaces.Clear();
		shapeCache.Dispose();
		atlas.Dispose();
		layoutCache.Clear();
		lastFrame = null;
		lock (probeLock)
			probeLibrary.Release();
		library.Release();
	}
}
