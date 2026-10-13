// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using FreeTypeSharp;
using Injure.Assets;
using Injure.Primitives;
using Injure.Gpu;
using static FreeTypeSharp.FT;

namespace Injure.Draw.Text;

public readonly record struct TextLayoutOptions(
	float MaxWidth = float.PositiveInfinity,
	TextWrapMode WrapMode = default,
	TextHorizontalAlign HorizontalAlign = default
) {
	public TextLayoutOptions() : this(float.PositiveInfinity) {}
};

public readonly record struct TextStyle(
	FontOptions FontOptions,
	SrgbColor32 Color,
	TextLayoutOptions LayoutOptions,
	string Locale = "und",
	string? LanguageBcp47 = null
) {
	public TextStyle(int fontSize, SrgbColor32 color, TextLayoutOptions? layoutOptions = null) : this(
		new FontOptions(fontSize),
		color,
		layoutOptions ?? new TextLayoutOptions()
	) {
	}
};

public sealed class TextCacheOptions {
	public int MaxShapeEntries { get; init; } = 4096;
	public int MaxShapeEstimatedCost { get; init; } = 8 * 1024 * 1024;

	public int MaxFallbackProbeEntries { get; init; } = 4096;
	public int MaxFallbackProbeEstimatedCost { get; init; } = 1 * 1024 * 1024;

	public int GlyphAtlasPageWidth { get; init; } = 1024;
	public int GlyphAtlasPageHeight { get; init; } = 1024;
	public int GlyphAtlasPadding { get; init; } = 1;
	public int MaxGlyphAtlasPages { get; init; } = 16;

	public int TrimEveryNOperations { get; init; } = 128;
}

// FreeType's library handle; font states hold a reference, since the asset store may reclaim them
// after the TextSystem is disposed
internal sealed unsafe class FtLibrary {
	private int refs = 1;

	public FT_LibraryRec_* Ptr { get; }

	public FtLibrary() {
		FT_LibraryRec_* l;
		FTException.Check(FT_Init_FreeType(&l));
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

public sealed unsafe class TextSystem : IDisposable {
	private readonly FtLibrary library = new();
	// FreeType libraries aren't thread-safe, and LoadFont runs on asset loading threads
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

	internal FtLibrary Library {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return library;
		}
	}

	internal TextSystem(GpuDevice gpuDevice, ITextItemizer? itemizer = null, TextCacheOptions? cacheOptions = null) {
		this.itemizer = itemizer ?? new DefaultTextItemizer();
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
	}

	internal IResolvedFont ResolveFont(Font font, int faceIndex, FontOptions opts) {
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		if (opts.PixelSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(opts), "PixelSize must be > 0");
		ResolvedFontKey key = new(FontSourceKind.Direct, font.Id, faceIndex, opts);
		if (!fonts.TryGetValue(key, out IResolvedFont? fnt)) {
			fnt = new ResolvedDirectFont(this, font, faceIndex, opts);
			fonts.Add(key, fnt);
		}
		return fnt;
	}

	internal IResolvedFont ResolveFont(Asset<Font> font, int faceIndex, FontOptions opts) {
		ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
		if (opts.PixelSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(opts), "PixelSize must be > 0");
		ResolvedFontKey key = new(FontSourceKind.Asset, font.SlotId, faceIndex, opts);
		if (!fonts.TryGetValue(key, out IResolvedFont? fnt)) {
			if (!assetFaces.TryGetValue((font.SlotId, faceIndex), out DerivedAsset<LoadedFontFace>? face)) {
				face = DerivedAsset.Create(font, f => new LoadedFontFace(f, faceIndex), $"font face {faceIndex} of {font.AssetId}");
				assetFaces.Add((font.SlotId, faceIndex), face);
			}
			fnt = new ResolvedAssetSourcedFont(this, font, face, faceIndex, opts);
			fonts.Add(key, fnt);
		}
		return fnt;
	}

	internal IResolvedFont ResolveFont(FontSpec spec, FontOptions opts) {
		return spec.SourceKind switch {
			FontSourceKind.Direct => ResolveFont(spec.Direct, spec.FaceIndex, opts),
			FontSourceKind.Asset => ResolveFont(spec.Asset, spec.FaceIndex, opts),
			_ => throw new UnreachableException(),
		};
	}

	internal ResolvedFontFallbackChain ResolveFallbackChain(FontFallbackChain fonts, FontOptions opts) {
		return new ResolvedFontFallbackChain(
			fonts.Id,
			ResolveFont(fonts.Primary, opts),
			fonts.Fallbacks.Select(f => ResolveFont(f, opts))
		);
	}

	internal LoadedFontFace GetOrCreateLoadedFace(Font font, int faceIndex) {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (!directFaces.TryGetValue((font.Id, faceIndex), out LoadedFontFace? face)) {
			face = new LoadedFontFace(font, faceIndex);
			directFaces.Add((font.Id, faceIndex), face);
		}
		return face;
	}

	// called when a font state is disposed, so the caches don't keep entries nothing can hit anymore
	internal void ForgetFontState(FontCacheToken token) {
		if (disposed)
			return;
		shapeCache.Forget(token);
		atlas.Forget(token);
	}

	internal TextMeasurement Measure(ResolvedFontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) {
		ObjectDisposedException.ThrowIf(disposed, this);
		TextLayoutPlan plan = TextLayouter.BuildPlan(itemizer, fallbackResolver, fonts, text, in style, TextLayoutPlanMode.MeasureOnly);
		return new TextMeasurement(plan.Width, plan.Height, plan.Lines.Length);
	}

	internal TextLayoutMeasurement MeasureLayout(ResolvedFontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) {
		ObjectDisposedException.ThrowIf(disposed, this);
		TextLayoutPlan plan = TextLayouter.BuildPlan(itemizer, fallbackResolver, fonts, text, in style, TextLayoutPlanMode.MeasureOnly);
		return new TextLayoutMeasurement(plan.Lines, plan.Width, plan.Height);
	}

	internal TextLayout Layout(ResolvedFontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) {
		ObjectDisposedException.ThrowIf(disposed, this);

		TextLayoutPlan plan = TextLayouter.BuildPlan(itemizer, fallbackResolver, fonts, text, in style, TextLayoutPlanMode.GlyphPlan);
		List<TextGlyph> glyphs = new(plan.Glyphs.Length);
		var lines = new TextLine[plan.Lines.Length];

		for (int lineIndex = 0; lineIndex < plan.Lines.Length; lineIndex++) {
			TextLine plannedLine = plan.Lines[lineIndex];
			int materializedStart = glyphs.Count;
			for (int i = plannedLine.GlyphStart; i < plannedLine.GlyphStart + plannedLine.GlyphCount; i++) {
				PlannedGlyph planned = plan.Glyphs[i];
				if (!atlas.TryGetOrCreate(planned.Font, planned.GlyphId, out GlyphAtlasEntry atlasEntry))
					continue;
				float x = planned.X + atlasEntry.BitmapLeft;
				float y = planned.Y - atlasEntry.BitmapTop;
				glyphs.Add(
					new TextGlyph(
						Page: atlasEntry.Page,
						SrcPixels: atlasEntry.SrcPixels,
						DstPixels: new RectF(x, y, atlasEntry.Width, atlasEntry.Height),
						Color: style.Color,
						GlyphId: planned.GlyphId,
						Cluster: planned.Cluster
					)
				);
			}
			lines[lineIndex] = plannedLine with {
				GlyphStart = materializedStart,
				GlyphCount = glyphs.Count - materializedStart,
			};
		}

		return new TextLayout(
			glyphs: glyphs.ToArray(),
			lines: lines,
			width: plan.Width,
			height: plan.Height,
			retainedPages: glyphs.Select(static g => g.Page).Distinct().ToArray()
		);
	}

	// thread-safe, since font asset creators call it from loading threads
	public Font LoadFont(byte[] data, string? debugName = null) {
		ArgumentNullException.ThrowIfNull(data);
		int faceCount;
		lock (probeLock) {
			// checked under the lock, which Dispose takes to free the probe library
			ObjectDisposedException.ThrowIf(disposed, this);
			fixed (byte* p = data) {
				FT_FaceRec_* probe = null;
				FTException.Check(FT_New_Memory_Face(probeLibrary.Ptr, p, data.Length, -1, &probe));
				try {
					faceCount = checked((int)probe->num_faces);
				} finally {
					if (probe is not null)
						FT_Done_Face(probe);
				}
			}
		}
		return new Font(data, debugName, faceCount);
	}

	public LiveText Make(FontSpec font, ReadOnlySpan<char> text, TextStyle style) =>
		new(this, new FontFallbackChain(font), text, style);

	public LiveText Make(FontFallbackChain fonts, ReadOnlySpan<char> text, TextStyle style) =>
		new(this, fonts, text, style);

	public TextMeasurement Measure(FontSpec font, ReadOnlySpan<char> text, in TextStyle style) =>
		Measure(ResolveFallbackChain(new FontFallbackChain(font), style.FontOptions), text, in style);

	public TextMeasurement Measure(FontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) =>
		Measure(ResolveFallbackChain(fonts, style.FontOptions), text, in style);

	public TextLayoutMeasurement MeasureLayout(FontSpec font, ReadOnlySpan<char> text, in TextStyle style) =>
		MeasureLayout(ResolveFallbackChain(new FontFallbackChain(font), style.FontOptions), text, in style);

	public TextLayoutMeasurement MeasureLayout(FontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) =>
		MeasureLayout(ResolveFallbackChain(fonts, style.FontOptions), text, in style);

	public TextLayout Layout(FontSpec font, ReadOnlySpan<char> text, in TextStyle style) =>
		Layout(ResolveFallbackChain(new FontFallbackChain(font), style.FontOptions), text, in style);

	public TextLayout Layout(FontFallbackChain fonts, ReadOnlySpan<char> text, in TextStyle style) =>
		Layout(ResolveFallbackChain(fonts, style.FontOptions), text, in style);

	internal void OnCacheActivity() {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (++opCounter < cacheOptions.TrimEveryNOperations)
			return;
		opCounter = 0;
		TrimCache();
	}

	public void ClearCache() {
		ObjectDisposedException.ThrowIf(disposed, this);
		shapeCache.Clear();
		fallbackProbeCache.Clear();
		atlas.Clear();
	}

	public void TrimCache() {
		ObjectDisposedException.ThrowIf(disposed, this);
		shapeCache.Trim();
		fallbackProbeCache.Trim();
		atlas.Trim();
	}

	// states of asset fonts are retired to the asset store and reclaimed by it, possibly after this;
	// they keep the FreeType library alive until then
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
		lock (probeLock)
			probeLibrary.Release();
		library.Release();
	}
}
