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
	/// <see cref="Canvas.Text(FontFallbackChain, ReadOnlySpan{char}, in TextStyle, System.Numerics.Vector2, Primitives.SrgbColor32)"/>
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
/// Text is laid out with <see cref="Layout(FontFallbackChain, string, in TextStyle)"/> and drawn
/// with <see cref="Canvas.Text(TextLayout, System.Numerics.Vector2, Primitives.SrgbColor32)"/>.
/// Alternatively, <see cref="Canvas"/> has integration with this type to do both at once as
/// <see cref="Canvas.Text(FontFallbackChain, ReadOnlySpan{char}, in TextStyle, System.Numerics.Vector2, Primitives.SrgbColor32)"/>,
/// which keeps the layouts it makes in a cache inside this <see cref="TextSystem"/>, dropping
/// entries that haven't been drawn for a while (see <see cref="TextCacheOptions.LayoutCacheFrames"/>).
/// </para>
/// <para>
/// Current limitations:
/// <list type="bullet">
/// <item><description>
/// Only horizontal text is supported, so e.g. vertical CJK layouts are impossible, and scripts
/// that require vertical writing, such as traditional Mongolian and Manchu script, are laid out
/// horizontally.
/// </description></item>
/// <item><description>
/// Dictionary-based line breaking is unsupported; scripts that need it (Thai, Lao, Khmer, Myanmar,
/// etc.) incorrectly only break at spaces and other explicit break opportunities, and a run of
/// words without one only breaks if it overflows the line (see <see cref="TextWrapMode.Greedy"/>).
/// </description></item>
/// <item><description>
/// Korean line-breaks between any two syllables, like CJK ideographs; there's no option to break
/// only between words.
/// </description></item>
/// <item><description>
/// There's no hyphenation. Soft hyphens (U+00AD) are treated as line break opportunities, but no
/// hyphen actually gets drawn at the break.
/// </description></item>
/// <item><description>
/// Only <c>'\n'</c> is supported as a paragraph separator; <c>'\r'</c>, U+2028 LINE SEPARATOR, and
/// U+2029 PARAGRAPH SEPARATOR aren't, though U+2028 and U+2029 still break the line.
/// </description></item>
/// <item><description>
/// <c>'\t'</c> has no tab stops. Additionally, <c>'\r'</c> and <c>'\t'</c> are drawn with whatever
/// glyph the font maps them to, or the missing-glyph glyph.
/// </description></item>
/// <item><description>
/// Text is shaped before it's broken into lines and isn't shaped again at line ends, so
/// contextual forms stay as they were in the unbroken text; for example, an Arabic word broken
/// because it overflows the line still has the joining forms at the break.
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
/// Fonts fall back only within a <see cref="FontFallbackChain"/>; system fonts are never used.
/// Variable fonts are used at their default instance, and there's no synthetic bold or italic.
/// </description></item>
/// <item><description>
/// Color glyphs, e.g. color emoji, are unsupported.
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
	/// <summary>
	/// Guards <see cref="probeLibrary"/>.
	/// </summary>
	/// <remarks>
	/// FreeType libraries aren't thread-safe, and <see cref="LoadFont(byte[], string?)"/> runs on
	/// asset loading threads.
	/// </remarks>
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
	/// <see cref="Canvas.Text(FontFallbackChain, ReadOnlySpan{char}, in TextStyle, System.Numerics.Vector2, Primitives.SrgbColor32)"/>,
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
	/// Thread-safe, since font asset creators call it from loading threads. The returned font can be
	/// used with any text system.
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

	internal LayoutResult RunLayouter(ResolvedFontFallbackChain chain, string text, in TextStyle style) =>
		TextLayouter.Layout(itemizer, fallbackResolver, chain, text, in style);

	/// <summary>
	/// Lays out text.
	/// </summary>
	/// <param name="fonts">
	/// The fonts to lay the text out with. A <see cref="FontSpec"/>, <see cref="FontData"/>, or
	/// <see cref="Asset{T}"/> of <see cref="FontData"/> converts to this implicitly.
	/// </param>
	/// <param name="text">
	/// The text. Each <c>'\n'</c> starts a new paragraph. Unpaired surrogates are replaced
	/// with U+FFFD REPLACEMENT CHARACTER.
	/// </param>
	/// <param name="style">How the text is sized and laid out.</param>
	/// <returns>The layout.</returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="fonts"/> or <paramref name="text"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="style"/>'s <see cref="TextStyle.Size"/> isn't positive and finite,
	/// or its <see cref="TextLayoutOptions.MaxWidth"/> is negative or NaN.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this text system has been disposed.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if one of <paramref name="fonts"/> is an asset and the calling thread isn't attached
	/// to its asset store.
	/// </exception>
	public TextLayout Layout(FontFallbackChain fonts, string text, in TextStyle style) {
		ArgumentNullException.ThrowIfNull(fonts);
		ArgumentNullException.ThrowIfNull(text);
		TextStyle.Validate(in style, nameof(style));
		ThrowIfDisposed();
		return new TextLayout(this, fonts, text, in style);
	}

	// ==========================================================================
	// layout cache

	internal TextLayout GetCachedLayout(FontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) {
		ThrowIfDisposed();
		if (layoutLookup.TryGetValue(text, out string? key, out List<CachedLayout>? list)) {
			foreach (CachedLayout c in list) {
				if (c.Layout.Fonts.Equals(fonts) && c.Layout.Style == style) {
					c.LastUsedFrame = frameNumber;
					return c.Layout;
				}
			}
		} else {
			key = new string(text);
			list = new List<CachedLayout>(1);
			layoutCache.Add(key, list);
		}
		TextLayout layout = new(this, fonts, key, in style);
		list.Add(new CachedLayout { Layout = layout, LastUsedFrame = frameNumber });
		return layout;
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
