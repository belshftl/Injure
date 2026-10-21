// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using HarfBuzzSharp;
using Injure.Assets;

namespace Injure.Draw.Text;

/// <summary>
/// A primary font face + a series of fallback faces, tried in order for characters the primary
/// face doesn't have.
/// </summary>
/// <remarks>
/// <para>
/// A character is drawn with the first face that has glyphs for its entire grapheme cluster, or
/// with the primary face's "missing" glyph (<c>.notdef</c>) if not a single one in the chain does.
/// </para>
/// <para>
/// Two chains are equal if they contain equal <see cref="FontSpec"/>s in the same order.
/// </para>
/// </remarks>
public sealed class FontFallbackChain : IEquatable<FontFallbackChain> {
	private readonly FontSpec[] allFonts;
	private readonly int hash;

	/// <summary>
	/// The face tried first.
	/// </summary>
	public FontSpec Primary => allFonts[0];

	/// <summary>
	/// The faces tried after <see cref="Primary"/>, in order.
	/// </summary>
	public ReadOnlySpan<FontSpec> Fallbacks => allFonts.AsSpan(1);

	/// <summary>
	/// <see cref="Primary"/> followed by <see cref="Fallbacks"/>.
	/// </summary>
	public ReadOnlySpan<FontSpec> AllFonts => allFonts;

	/// <summary>
	/// Creates a chain.
	/// </summary>
	/// <param name="primary">The face tried first.</param>
	/// <param name="fallbacks">The faces tried after <paramref name="primary"/>, in order.</param>
	public FontFallbackChain(FontSpec primary, params ReadOnlySpan<FontSpec> fallbacks) {
		ArgumentInvalidDefaultException.ThrowIfDefault(primary);
		ArgumentInvalidDefaultException.ThrowIfAnyDefault(fallbacks, nameof(fallbacks));
		allFonts = new FontSpec[fallbacks.Length + 1];
		allFonts[0] = primary;
		fallbacks.CopyTo(allFonts.AsSpan(1));
		HashCode h = new();
		foreach (FontSpec f in allFonts)
			h.Add(f);
		hash = h.ToHashCode();
	}

	/// <summary>
	/// Creates a chain with <paramref name="font"/> and no fallbacks.
	/// </summary>
	public static implicit operator FontFallbackChain(FontSpec font) => new(font);

	/// <summary>
	/// Creates a chain with the first face of <paramref name="font"/> and no fallbacks.
	/// </summary>
	public static implicit operator FontFallbackChain(FontData font) => new(font);

	/// <summary>
	/// Creates a chain with the first face of <paramref name="font"/> and no fallbacks.
	/// </summary>
	public static implicit operator FontFallbackChain(Asset<FontData> font) => new(font);

	public bool Equals(FontFallbackChain? other) =>
		other is not null && (ReferenceEquals(this, other) || hash == other.hash && allFonts.AsSpan().SequenceEqual(other.allFonts));
	public override bool Equals(object? obj) => Equals(obj as FontFallbackChain);
	public override int GetHashCode() => hash;
	public static bool operator ==(FontFallbackChain? left, FontFallbackChain? right) =>
		left is null ? right is null : left.Equals(right);
	public static bool operator !=(FontFallbackChain? left, FontFallbackChain? right) => !(left == right);
}

internal sealed class FontChainKey(FontCacheToken[] tokens) : IEquatable<FontChainKey> {
	private readonly FontCacheToken[] tokens = tokens;
	private readonly int hash = computeHash(tokens);

	private static int computeHash(FontCacheToken[] tokens) {
		HashCode h = new();
		foreach (FontCacheToken t in tokens)
			h.Add(t);
		return h.ToHashCode();
	}

	public bool Equals(FontChainKey? other) =>
		other is not null && hash == other.hash && tokens.AsSpan().SequenceEqual(other.tokens);
	public override bool Equals(object? obj) => Equals(obj as FontChainKey);
	public override int GetHashCode() => hash;
	public static bool operator ==(FontChainKey? left, FontChainKey? right) =>
		left is null ? right is null : left.Equals(right);
	public static bool operator !=(FontChainKey? left, FontChainKey? right) => !(left == right);
}

internal sealed class ResolvedFontFallbackChain {
	public readonly IResolvedFont[] Fonts;
	public readonly FontRasterOptions[] Raster;
	public readonly ResolvedFontState[] States;
	public readonly FontCacheToken[] Tokens;
	public readonly FontChainKey Key;

	public ResolvedFontFallbackChain(IResolvedFont[] fonts, FontRasterOptions[] raster) {
		Fonts = fonts;
		Raster = raster;
		States = new ResolvedFontState[fonts.Length];
		Tokens = new FontCacheToken[fonts.Length];
		for (int i = 0; i < fonts.Length; i++) {
			States[i] = fonts[i].GetState();
			Tokens[i] = States[i].CacheToken;
		}
		Key = new FontChainKey(Tokens);
	}

	public IResolvedFont Primary => Fonts[0];

	public bool IsStale() {
		for (int i = 0; i < Fonts.Length; i++)
			if (Fonts[i].GetCacheToken() != Tokens[i])
				return true;
		return false;
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct FallbackProbeKey(
	FontChainKey Chain,
	string Text,
	Direction Direction,
	Script? Script,
	string? LanguageBcp47
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Text is null;
}

internal sealed class FallbackProbeCache(TextSystem text, int maxEntries, int maxEstimatedCost) {
	private sealed class Entry {
		public required int FontIndex;
		public required ulong LastUseStamp;
		public required int EstimatedCost;
	}

	private readonly TextSystem text = text;
	private readonly int maxEntries = maxEntries;
	private readonly int maxEstimatedCost = maxEstimatedCost;
	private readonly Dictionary<FallbackProbeKey, Entry> cache = new();
	private ulong nextUseStamp = 0; // first will be 1 since this gets incremented upfront
	private int totalEstimatedCost = 0;

	public void Clear() {
		cache.Clear();
		totalEstimatedCost = 0;
	}

	public bool TryGet(FallbackProbeKey key, out int fontIndex) {
		if (cache.TryGetValue(key, out Entry? ent)) {
			ent.LastUseStamp = ++nextUseStamp;
			fontIndex = ent.FontIndex;
			return true;
		}
		fontIndex = default;
		return false;
	}

	public void Set(FallbackProbeKey key, int fontIndex) {
		if (cache.Remove(key, out Entry? ent))
			totalEstimatedCost -= ent.EstimatedCost;
		int est = estimate(key);
		cache[key] = new Entry {
			FontIndex = fontIndex,
			LastUseStamp = ++nextUseStamp,
			EstimatedCost = est,
		};
		totalEstimatedCost += est;
		text.OnCacheActivity();
		Trim();
	}

	public void Trim() {
		if (cache.Count <= maxEntries && totalEstimatedCost <= maxEstimatedCost)
			return;
		foreach (FallbackProbeKey key in cache
			.OrderBy(static kvp => kvp.Value.LastUseStamp)
			.ThenByDescending(static kvp => kvp.Value.EstimatedCost)
			.Select(static kvp => kvp.Key)) {
			if (cache.Count <= maxEntries && totalEstimatedCost <= maxEstimatedCost)
				break;
			totalEstimatedCost -= cache[key].EstimatedCost;
			cache.Remove(key);
		}
	}

	private static int estimate(FallbackProbeKey key) =>
		(key.Text.Length * sizeof(char))
		+ 80; // very rough estimate of extra object overhead, to avoid pretending small entries are free
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct ResolvedItem(
	int FontIndex,
	TextItem Item,
	ShapedRun ShapedRun
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => ShapedRun is null;
}

internal sealed class FallbackResolver(ShapeCache shapeCache, FallbackProbeCache probeCache) {
	private readonly ShapeCache shapeCache = shapeCache;
	private readonly FallbackProbeCache probeCache = probeCache;

	public void ResolveItems(ResolvedFontFallbackChain fonts, TextItem item, List<ResolvedItem> dst) {
		GraphemeSpan[] graphemes = TextAnalysis.GetGraphemeSpans(item.Text);
		if (graphemes.Length == 0)
			return;
		int spanStart = 0;
		int spanFont = resolveFontIndex(fonts, item, graphemes[0]);
		for (int i = 1; i <= graphemes.Length; i++) {
			int font = i < graphemes.Length ? resolveFontIndex(fonts, item, graphemes[i]) : -1;
			if (font == spanFont)
				continue;
			int start = graphemes[spanStart].Start;
			int limit = graphemes[i - 1].Start + graphemes[i - 1].Length;
			TextItem merged = item with {
				SourceStart = item.SourceStart + start,
				Text = item.Text[start..limit],
			};
			dst.Add(new ResolvedItem(spanFont, merged, shapeCache.GetOrCreate(fonts.Fonts[spanFont], merged)));
			spanStart = i;
			spanFont = font;
		}
	}

	private int resolveFontIndex(ResolvedFontFallbackChain fonts, TextItem parentItem, GraphemeSpan grapheme) {
		if (fonts.Fonts.Length == 1)
			return 0;
		string text = parentItem.Text.Substring(grapheme.Start, grapheme.Length);
		FallbackProbeKey key = new(
			Chain: fonts.Key,
			Text: text,
			Direction: parentItem.Properties.Direction ?? throw new InternalStateException("fallback probing requires an explicit direction"),
			Script: parentItem.Properties.Script,
			LanguageBcp47: parentItem.Properties.LanguageBcp47
		);
		if (probeCache.TryGet(key, out int fontIndex))
			return fontIndex;
		TextItem graphemeItem = parentItem with {
			SourceStart = parentItem.SourceStart + grapheme.Start,
			Text = text,
		};
		// shape without caching, single graphemes would only crowd out the shape cache
		fontIndex = 0;
		for (int i = 0; i < fonts.Fonts.Length; i++) {
			if (noNotdefs(shapeCache.ShapeUncached(fonts.Fonts[i], graphemeItem))) {
				fontIndex = i;
				break;
			}
		}
		probeCache.Set(key, fontIndex);
		return fontIndex;
	}

	private static bool noNotdefs(ShapedRun shaped) {
		foreach (ShapedGlyph glyph in shaped.Glyphs)
			if (glyph.GlyphId == 0)
				return false;
		return true;
	}
}
