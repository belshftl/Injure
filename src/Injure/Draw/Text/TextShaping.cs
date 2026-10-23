// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using HarfBuzzSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace Injure.Draw.Text;

/// <remarks>
/// The <see langword="default"/> value is valid and specifies no properties.
/// </remarks>
internal readonly record struct TextSegmentProperties(
	Direction? Direction = null,
	Script? Script = null,
	string? LanguageBcp47 = null
);

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct TextItem(
	int SourceStart,
	string Text,
	TextSegmentProperties Properties,
	bool GuessSegmentProperties = false
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Text is null;
}

/// <summary>
/// A glyph of a <see cref="ShapedRun"/>.
/// </summary>
/// <remarks>
/// <para>
/// Positions are in em units, i.e. fractions of the font size, since layout is unhinted and scales
/// linearly with the size.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is glyph 0 with no offset or advance.
/// </para>
/// </remarks>
internal readonly record struct ShapedGlyph(
	uint GlyphId,
	uint Cluster,
	float XOffset,
	float YOffset,
	float XAdvance,
	float YAdvance
) {
	public static readonly int Size = Unsafe.SizeOf<ShapedGlyph>();
}

/// <remarks>
/// The <see langword="default"/> value is valid and is an empty cluster starting at 0.
/// </remarks>
internal readonly record struct ShapedCluster(
	int GlyphStart,
	int GlyphCount,
	int SourceStart,
	int SourceLength,
	float Width,
	bool IsWhitespaceOnly
) {
	public static readonly int Size = Unsafe.SizeOf<ShapedCluster>();
	public int SourceLimit => SourceStart + SourceLength;
}

internal sealed class ShapedRun {
	public required string Text { get; init; }
	public required TextSegmentProperties Properties { get; init; }
	public required bool GuessSegmentProperties { get; init; }
	public required ShapedGlyph[] Glyphs { get; init; }
	/// <summary>
	/// The run's clusters, sorted by <see cref="ShapedCluster.SourceStart"/>.
	/// </summary>
	/// <remarks>
	/// Each cluster's glyphs are contiguous in <see cref="Glyphs"/> and in visual order.
	/// </remarks>
	public required ShapedCluster[] SourceOrderClusters { get; init; }
	public required float Width { get; init; }
}

internal interface ITextItemizer {
	TextItem[] Itemize(ReadOnlySpan<char> text, int sourceStart, Direction direction, string? languageBcp47);
}

internal sealed class DefaultTextItemizer : ITextItemizer {
	public static readonly DefaultTextItemizer Instance = new();

	private DefaultTextItemizer() {}

	public TextItem[] Itemize(ReadOnlySpan<char> text, int sourceStart, Direction direction, string? languageBcp47) =>
		TextAnalysis.ItemizeByScript(text, sourceStart, direction, languageBcp47);
}

internal sealed class ShapeCache(TextSystem text, int maxEntries, int maxEstimatedCost) : IDisposable {
	/// <remarks>
	/// The <see langword="default"/> value is invalid.
	/// </remarks>
	private readonly record struct Key(
		FontCacheToken FontCacheToken,
		string Text,
		TextSegmentProperties Properties,
		bool GuessSegmentProperties
	) : IHasInvalidDefault {
		bool IHasInvalidDefault.IsDefault => Text is null;
	}

	private sealed class Entry {
		public required ShapedRun Shaped;
		public required ulong LastUseStamp;
		public required int EstimatedCost;
	}

	private readonly TextSystem text = text;
	private readonly int maxEntries = maxEntries;
	private readonly int maxEstimatedCost = maxEstimatedCost;
	private readonly Dictionary<Key, Entry> cache = new();
	private readonly Dictionary<string, Language> langs = new(StringComparer.OrdinalIgnoreCase);
	private ulong nextUseStamp = 0; // first will be 1 since this gets incremented upfront
	private int totalEstimatedCost = 0;

	public void Clear() {
		cache.Clear();
		totalEstimatedCost = 0;
	}

	public void Forget(FontCacheToken token) {
		foreach (Key key in cache.Keys.Where(k => k.FontCacheToken == token).ToArray()) {
			totalEstimatedCost -= cache[key].EstimatedCost;
			cache.Remove(key);
		}
	}

	public ShapedRun ShapeUncached(IResolvedFont font, in TextItem item) => shape(font, in item);

	public ShapedRun GetOrCreate(IResolvedFont font, in TextItem item) {
		Key key = new(
			FontCacheToken: font.GetCacheToken(),
			Text: item.Text,
			Properties: item.Properties,
			GuessSegmentProperties: item.GuessSegmentProperties
		);
		if (cache.TryGetValue(key, out Entry? ent)) {
			ent.LastUseStamp = ++nextUseStamp;
			return ent.Shaped;
		}
		ShapedRun shaped = shape(font, in item);
		int est = estimate(shaped, in item);
		cache.Add(
			key,
			new Entry {
				Shaped = shaped,
				LastUseStamp = ++nextUseStamp,
				EstimatedCost = est,
			}
		);
		totalEstimatedCost += est;
		text.OnCacheActivity();
		Trim();
		return shaped;
	}

	private ShapedRun shape(IResolvedFont font, in TextItem item) {
		ResolvedFontState st = font.GetState();
		using Buffer buf = new();
		buf.AddUtf16(item.Text.AsSpan());
		if (item.GuessSegmentProperties) {
			buf.GuessSegmentProperties();
		} else {
			if (item.Properties.Direction is Direction d)
				buf.Direction = d;
			if (item.Properties.Script is Script s)
				buf.Script = s;
			// "und" is the unspecified language, which leaving the buffer's language unset means
			if (!string.IsNullOrWhiteSpace(item.Properties.LanguageBcp47) && !string.Equals(item.Properties.LanguageBcp47, "und", StringComparison.OrdinalIgnoreCase)) {
				if (!langs.TryGetValue(item.Properties.LanguageBcp47, out Language? l)) {
					l = new Language(item.Properties.LanguageBcp47);
					langs.Add(item.Properties.LanguageBcp47, l);
				}
				buf.Language = l;
			}
		}
		st.HbFont.Shape(buf);
		GlyphInfo[] infos = buf.GlyphInfos;
		GlyphPosition[] positions = buf.GlyphPositions;
		var glyphs = new ShapedGlyph[buf.Length];
		float perUnit = 1f / st.UnitsPerEm;
		float w = 0f;
		for (int i = 0; i < buf.Length; i++) {
			glyphs[i] = new ShapedGlyph(
				GlyphId: infos[i].Codepoint,
				Cluster: infos[i].Cluster,
				XOffset: positions[i].XOffset * perUnit,
				YOffset: positions[i].YOffset * perUnit,
				XAdvance: positions[i].XAdvance * perUnit,
				YAdvance: positions[i].YAdvance * perUnit
			);
			w += glyphs[i].XAdvance;
		}
		(TextSegmentProperties properties, ShapedCluster[] sourceOrderClusters) = buildClusters(item, buf, glyphs);
		return new ShapedRun {
			Text = item.Text,
			Properties = properties,
			GuessSegmentProperties = item.GuessSegmentProperties,
			Glyphs = glyphs,
			SourceOrderClusters = sourceOrderClusters,
			Width = w,
		};
	}

	private static (TextSegmentProperties Properties, ShapedCluster[] SourceOrderClusters) buildClusters(
		in TextItem item,
		Buffer buf,
		ShapedGlyph[] glyphs
	) {
		List<ShapedCluster> glyphOrderClusters = new();
		if (glyphs.Length != 0) {
			HashSet<uint> uniqueClusters = new();
			for (int i = 0; i < glyphs.Length; i++)
				uniqueClusters.Add(glyphs[i].Cluster);
			uint[] clusterStarts = uniqueClusters.ToArray();
			Array.Sort(clusterStarts);

			Dictionary<uint, int> nextClusterMap = new(clusterStarts.Length);
			for (int i = 0; i < clusterStarts.Length; i++) {
				int next = i + 1 < clusterStarts.Length ? checked((int)clusterStarts[i + 1]) : item.Text.Length;
				nextClusterMap.Add(clusterStarts[i], next);
			}

			int glyphStart = 0;
			while (glyphStart < glyphs.Length) {
				uint cluster = glyphs[glyphStart].Cluster;
				float clusterWidth = glyphs[glyphStart].XAdvance;
				int glyphCount = 1;
				for (; glyphStart + glyphCount < glyphs.Length && glyphs[glyphStart + glyphCount].Cluster == cluster; glyphCount++)
					clusterWidth += glyphs[glyphStart + glyphCount].XAdvance;
				int sourceStart = Math.Clamp(checked((int)cluster), 0, item.Text.Length);
				int sourceLimit = Math.Max(Math.Clamp(nextClusterMap[cluster], 0, item.Text.Length), sourceStart);
				ReadOnlySpan<char> sourceText = item.Text.AsSpan(sourceStart, sourceLimit - sourceStart);
				glyphOrderClusters.Add(
					new ShapedCluster(
						GlyphStart: glyphStart,
						GlyphCount: glyphCount,
						SourceStart: sourceStart,
						SourceLength: sourceLimit - sourceStart,
						Width: clusterWidth,
						IsWhitespaceOnly: isWsOnly(sourceText)
					)
				);
				glyphStart += glyphCount;
			}
		}
		ShapedCluster[] sourceOrderArray = glyphOrderClusters.ToArray();
		Array.Sort(
			sourceOrderArray,
			static (a, b) => {
				int n = a.SourceStart.CompareTo(b.SourceStart);
				return n != 0 ? n : a.GlyphStart.CompareTo(b.GlyphStart);
			}
		);
		TextSegmentProperties props = !item.GuessSegmentProperties
			? item.Properties
			: new TextSegmentProperties(Direction: buf.Direction, Script: buf.Script, LanguageBcp47: item.Properties.LanguageBcp47);
		return (props, sourceOrderArray);
	}

	private static bool isWsOnly(ReadOnlySpan<char> text) {
		if (text.IsEmpty)
			return false;
		for (int i = 0; i < text.Length; i++)
			if (!char.IsWhiteSpace(text[i]))
				return false;
		return true;
	}

	private static int estimate(ShapedRun shaped, in TextItem item) =>
		(item.Text.Length * sizeof(char))
		+ (shaped.Glyphs.Length * ShapedGlyph.Size)
		+ (shaped.SourceOrderClusters.Length * ShapedCluster.Size)
		+ 144; // very rough estimate of extra object overhead, to avoid pretending small entries are free

	public void Trim() {
		if (cache.Count <= maxEntries && totalEstimatedCost <= maxEstimatedCost)
			return;
		foreach (
			Key key in cache
				.OrderBy(static kvp => kvp.Value.LastUseStamp)
				.ThenByDescending(static kvp => kvp.Value.EstimatedCost)
				.Select(static kvp => kvp.Key)
		) {
			if (cache.Count <= maxEntries && totalEstimatedCost <= maxEstimatedCost)
				break;
			totalEstimatedCost -= cache[key].EstimatedCost;
			cache.Remove(key);
		}
	}

	public void Dispose() {
		foreach (Language lang in langs.Values)
			lang.Dispose();
		langs.Clear();
		cache.Clear();
	}
}
