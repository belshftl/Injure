// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HarfBuzzSharp;
using static Injure.Draw.Text.Fribidi;

namespace Injure.Draw.Text;

internal static unsafe partial class Fribidi {
	[LibraryImport("fribidi")]
	public static partial void fribidi_get_bidi_types(uint* str, int len, uint* btypes);

	[LibraryImport("fribidi")]
	public static partial uint fribidi_get_par_direction(uint* bidiTypes, int len);

	[LibraryImport("fribidi")]
	public static partial sbyte fribidi_get_par_embedding_levels_ex(uint* bidiTypes, uint* bracketTypes, int len, uint* pbaseDir, sbyte* embeddingLevels);

	[LibraryImport("fribidi")]
	public static partial sbyte fribidi_reorder_line(uint flags, uint* bidiTypes, int len, int off, uint baseDir, sbyte* embeddingLevels, uint* visualStr, int* map);

	[LibraryImport("fribidi")]
	public static partial uint fribidi_get_bracket(uint ch);
}

internal static unsafe partial class Unibreak {
	public const sbyte LINEBREAK_MUSTBREAK = 0;
	public const sbyte LINEBREAK_ALLOWBREAK = 1;
	public const sbyte LINEBREAK_NOBREAK = 2;
	public const sbyte LINEBREAK_INSIDEACHAR = 3;
	public const sbyte LINEBREAK_INDETERMINATE = 4;

	// void set_linebreaks_utf16(const utf16_t *s, size_t len, const char *lang, char *brks);
	[LibraryImport("ijmisc", StringMarshalling = StringMarshalling.Utf8)]
	public static partial void set_linebreaks_utf16(char* s, nuint len, string? lang, sbyte* brks);

	public static void SetLineBreaks(string text, Span<sbyte> breaks, string? lang = null) {
		if (breaks.Length < text.Length)
			throw new InternalStateException("line breaks buffer too small");
		fixed (char* pText = text)
		fixed (sbyte* pBreaks = breaks)
			set_linebreaks_utf16(pText, (nuint)text.Length, lang, pBreaks);
	}
}

/// <remarks>
/// The <see langword="default"/> value is valid and is an empty span starting at 0.
/// </remarks>
internal readonly record struct GraphemeSpan(
	int Start,
	int Length
);

internal static unsafe class TextAnalysis {
	public sealed class ParagraphAnalysis : IDisposable {
		// TODO: a way to pass FRIBIDI_FLAG_REORDER_NSM for rtl combining mark placement
		private const uint fribidiFlags = 0;

		private bool disposed = false;
		/// <summary>
		/// Copy of <see cref="Levels"/> that <c>fribidi_reorder_line</c> reorders in place.
		/// </summary>
		/// <remarks>
		/// <c>fribidi_reorder_line</c> indexes this and <see cref="lineMap"/> by position in the
		/// paragraph and only touches the line's part, so one copy of each serves every line of the
		/// paragraph.
		/// </remarks>
		private sbyte[]? lineLevels;

		/// <summary>
		/// Visual-to-logical map of the line last passed to <see cref="GetVisualRunsForLine"/>.
		/// </summary>
		/// <remarks>
		/// See <see cref="lineLevels"/>.
		/// </remarks>
		private int[]? lineMap;

		public string Text { get; }
		public int ScalarLength { get; }
		public int[] ScalarStartsUtf16 { get; }
		public uint[] Scalars { get; }
		public uint[] BidiTypes { get; }
		public uint[] BracketTypes { get; }
		public sbyte[] Levels { get; }
		public uint BaseDir { get; }

		private ParagraphAnalysis(
			string text,
			int scalarLength,
			int[] scalarStartsUtf16,
			uint[] scalars,
			uint[] bidiTypes,
			uint[] bracketTypes,
			sbyte[] levels,
			uint baseDir
		) {
			Text = text;
			ScalarLength = scalarLength;
			ScalarStartsUtf16 = scalarStartsUtf16;
			Scalars = scalars;
			BidiTypes = bidiTypes;
			BracketTypes = bracketTypes;
			Levels = levels;
			BaseDir = baseDir;
		}

		public static ParagraphAnalysis Create(string text) {
			int estimatedScalars = text.Length; // XXX this is an upper-bound estimate that may be too blunt
			int[] scalarStartsUtf16 = ArrayPool<int>.Shared.Rent(estimatedScalars + 1);
			uint[] scalars = ArrayPool<uint>.Shared.Rent(estimatedScalars);
			uint[] bidiTypes = ArrayPool<uint>.Shared.Rent(estimatedScalars);
			uint[] bracketTypes = ArrayPool<uint>.Shared.Rent(estimatedScalars);
			sbyte[] levels = ArrayPool<sbyte>.Shared.Rent(estimatedScalars);
			int scalarCount = 0;
			int utf16Index = 0;
			scalarStartsUtf16[0] = 0;

			while (utf16Index < text.Length) {
				if (Rune.DecodeFromUtf16(text.AsSpan(utf16Index), out Rune rune, out int consumed) != OperationStatus.Done)
					throw new InternalStateException($"invalid utf-16 at index {utf16Index}, which layout should have replaced");
				scalars[scalarCount] = (uint)rune.Value;
				utf16Index += consumed;
				scalarCount++;
				scalarStartsUtf16[scalarCount] = utf16Index;
			}

			uint baseDir;
			fixed (uint* pScalars = scalars)
			fixed (uint* pBidiTypes = bidiTypes)
			fixed (uint* pBracketTypes = bracketTypes)
			fixed (sbyte* pLevels = levels) {
				fribidi_get_bidi_types(pScalars, scalarCount, pBidiTypes);
				for (int i = 0; i < scalarCount; i++)
					pBracketTypes[i] = fribidi_get_bracket(pScalars[i]);
				baseDir = fribidi_get_par_direction(pBidiTypes, scalarCount);
				if (fribidi_get_par_embedding_levels_ex(pBidiTypes, pBracketTypes, scalarCount, &baseDir, pLevels) == 0)
					throw new InvalidOperationException("fribidi_get_par_embedding_levels_ex failed");
			}
			return new ParagraphAnalysis(text, scalarCount, scalarStartsUtf16, scalars, bidiTypes, bracketTypes, levels, baseDir);
		}

		public void GetLogicalRuns(List<LogicalBidiRun> dst) {
			if (disposed)
				throw new InternalStateException("ParagraphAnalysis already disposed");
			int scalarStart = 0;
			while (scalarStart < ScalarLength) {
				bool rtl = isRtlLevel(Levels[scalarStart]);
				int scalarLimit = scalarStart + 1;
				while (scalarLimit < ScalarLength && isRtlLevel(Levels[scalarLimit]) == rtl)
					scalarLimit++;
				int utf16Start = ScalarStartsUtf16[scalarStart];
				int utf16Limit = ScalarStartsUtf16[scalarLimit];
				dst.Add(
					new LogicalBidiRun(
						Start: utf16Start,
						Length: utf16Limit - utf16Start,
						Direction: rtl ? Direction.RightToLeft : Direction.LeftToRight
					)
				);
				scalarStart = scalarLimit;
			}
		}

		public void GetVisualRunsForLine(List<VisualBidiRun> dst, int lineStartUtf16, int lineLimitUtf16) {
			if (disposed)
				throw new InternalStateException("ParagraphAnalysis already disposed");
			int lineScalarStart = utf16ToScalarBoundary(lineStartUtf16);
			int lineScalarLimit = utf16ToScalarBoundary(lineLimitUtf16);
			int lineScalarLength = lineScalarLimit - lineScalarStart;
			if (lineScalarLength <= 0)
				return;

			if (lineLevels is null || lineMap is null) {
				lineLevels = ArrayPool<sbyte>.Shared.Rent(ScalarLength);
				Array.Copy(Levels, 0, lineLevels, 0, ScalarLength);
				lineMap = ArrayPool<int>.Shared.Rent(ScalarLength);
			}
			sbyte[] levels = lineLevels;
			int[] map = lineMap;
			for (int i = lineScalarStart; i < lineScalarLimit; i++)
				map[i] = i;

			fixed (uint* pBidiTypes = BidiTypes)
			fixed (sbyte* pLevels = levels)
			fixed (int* pMap = map) {
				if (fribidi_reorder_line(
					fribidiFlags,
					pBidiTypes,
					lineScalarLength,
					lineScalarStart,
					BaseDir,
					pLevels,
					null,
					pMap
				) == 0)
					throw new InvalidOperationException("fribidi_reorder_line failed");
			}

			int visualIndex = lineScalarStart;
			while (visualIndex < lineScalarLimit) {
				int firstLogical = map[visualIndex];
				bool rtl = isRtlLevel(levels[firstLogical]);
				int delta = rtl ? -1 : 1;
				int minLogical = firstLogical;
				int maxLogical = firstLogical;
				int prevLogical = firstLogical;
				int nextVisual = visualIndex + 1;
				while (nextVisual < lineScalarLimit) {
					int logical = map[nextVisual];
					if (isRtlLevel(levels[logical]) != rtl)
						break;
					if (logical != prevLogical + delta)
						break;
					prevLogical = logical;
					minLogical = Math.Min(minLogical, logical);
					maxLogical = Math.Max(maxLogical, logical);
					nextVisual++;
				}
				int utf16Start = ScalarStartsUtf16[minLogical];
				int utf16Limit = ScalarStartsUtf16[maxLogical + 1];
				dst.Add(
					new VisualBidiRun(
						Start: utf16Start,
						Length: utf16Limit - utf16Start,
						Direction: rtl ? Direction.RightToLeft : Direction.LeftToRight
					)
				);
				visualIndex = nextVisual;
			}
		}

		private int utf16ToScalarBoundary(int utf16Offset) {
			int idx = Array.BinarySearch(ScalarStartsUtf16, 0, ScalarLength + 1, utf16Offset);
			if (idx < 0)
				throw new InternalStateException($"offset {utf16Offset} is not on a scalar boundary");
			return idx;
		}

		public void Dispose() {
			if (disposed)
				return;
			disposed = true;
			ArrayPool<int>.Shared.Return(ScalarStartsUtf16);
			ArrayPool<uint>.Shared.Return(Scalars);
			ArrayPool<uint>.Shared.Return(BidiTypes);
			ArrayPool<uint>.Shared.Return(BracketTypes);
			ArrayPool<sbyte>.Shared.Return(Levels);
			if (lineLevels is not null)
				ArrayPool<sbyte>.Shared.Return(lineLevels);
			if (lineMap is not null)
				ArrayPool<int>.Shared.Return(lineMap);
		}
	}

	private static readonly UnicodeFunctions hbUnicode = UnicodeFunctions.Default;

	/// <summary>
	/// Replaces unpaired surrogates with U+FFFD, which keeps the length and every index the same.
	/// </summary>
	public static string ReplaceUnpairedSurrogates(string s) {
		// the vast majority of inputs won't have unpaired surrogates
		char[]? chars = null;
		for (int i = 0; i < s.Length; i++) {
			char c = s[i];
			if (!char.IsSurrogate(c))
				continue;
			if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) {
				i++;
				continue;
			}
			chars ??= s.ToCharArray();
			chars[i] = '\ufffd';
		}
		return chars is null ? s : new string(chars);
	}

	public static GraphemeSpan[] GetGraphemeSpans(string text) {
		if (text.Length == 0)
			return [];
		int[] starts = StringInfo.ParseCombiningCharacters(text);
		var spans = new GraphemeSpan[starts.Length];
		for (int i = 0; i < starts.Length; i++) {
			int start = starts[i];
			int limit = i + 1 < starts.Length ? starts[i + 1] : text.Length;
			spans[i] = new GraphemeSpan(start, limit - start);
		}
		return spans;
	}

	/// <summary>
	/// Converts a BCP 47 language tag to the language string libunibreak expects.
	/// </summary>
	/// <remarks>
	/// libunibreak compares the first two characters with the languages it has rules for (and with
	/// <c>zh</c>/<c>ja</c>/<c>ko</c> for CJK handling), case-sensitively, and checks for a
	/// <c>-strict</c> suffix. So, for example, a three-letter subtag like <c>kok</c> (Konkani) would
	/// falsely match <c>ko</c> (Korean).
	/// </remarks>
	public static string? UnibreakLanguage(string? tag) {
		if (string.IsNullOrEmpty(tag))
			return null;
		string[] subtags = tag.ToLowerInvariant().Split('-');
		string? lang = subtags[0].Length == 2 ? subtags[0] : null;
		bool strict = false;
		// the u extension's keywords run until the next singleton subtag
		int u = Array.IndexOf(subtags, "u");
		if (u > 0) {
			for (int i = u + 1; i + 1 < subtags.Length && subtags[i].Length > 1; i++) {
				if (subtags[i] == "lb" && subtags[i + 1] == "strict") {
					strict = true;
					break;
				}
			}
		}
		return strict ? (lang ?? "und") + "-strict" : lang;
	}

	public static LineBreakOpportunity[] GetLineBreaks(string text, string? unibreakLanguage) {
		if (text.Length == 0)
			return [];
		Span<sbyte> raw = text.Length <= 2048 ? stackalloc sbyte[text.Length] : new sbyte[text.Length];
		Unibreak.SetLineBreaks(text, raw, unibreakLanguage);
		List<LineBreakOpportunity> breaks = new();
		for (int i = 0; i < raw.Length; i++) {
			LineBreakKind kind = raw[i] switch {
				Unibreak.LINEBREAK_MUSTBREAK => LineBreakKind.Hard,
				Unibreak.LINEBREAK_ALLOWBREAK => LineBreakKind.Soft,
				_ => LineBreakKind.None,
			};
			if (kind != LineBreakKind.None)
				breaks.Add(new LineBreakOpportunity(i + 1, kind));
		}
		return breaks.ToArray();
	}

	public static LogicalBidiRun[] GetLogicalBidiRuns(string text) {
		if (text.Length == 0)
			return [];
		using var para = ParagraphAnalysis.Create(text);
		List<LogicalBidiRun> l = new();
		para.GetLogicalRuns(l);
		return l.ToArray();
	}

	public static VisualBidiRun[] GetVisualBidiRunsForLine(string text, int lineStart, int lineLimit) {
		if (lineStart >= lineLimit)
			return [];
		using var para = ParagraphAnalysis.Create(text);
		List<VisualBidiRun> l = new();
		para.GetVisualRunsForLine(l, lineStart, lineLimit);
		return l.ToArray();
	}

	public static TextItem[] ItemizeByScript(ReadOnlySpan<char> text, int sourceStart, Direction direction, string? languageBcp47) {
		static void add(
			List<TextItem> dst,
			ReadOnlySpan<char> text,
			int sourceStart,
			int runStart,
			int runLength,
			Direction direction,
			Script? script,
			string? languageBcp47
		) {
			if (runLength <= 0)
				return;
			dst.Add(
				new TextItem(
					SourceStart: sourceStart + runStart,
					Text: new string(text.Slice(runStart, runLength)),
					Properties: new TextSegmentProperties(direction, script, languageBcp47),
					GuessSegmentProperties: false
				)
			);
		}

		if (text.IsEmpty)
			return [];
		List<TextItem> items = new();
		int currentRunStart = -1;
		Script? currentScript = null;
		int pendingPrefixStart = -1;
		int index = 0;
		while (index < text.Length) {
			if (Rune.DecodeFromUtf16(text[index..], out Rune rune, out int runeLength) != OperationStatus.Done)
				throw new InternalStateException($"invalid utf-16 at index {index}, which layout should have replaced");
			Script script = hbUnicode.GetScript(rune.Value);
			if (script == Script.Common || script == Script.Inherited || script == Script.Unknown) {
				if (currentRunStart < 0 && pendingPrefixStart < 0)
					pendingPrefixStart = index;
			} else if (currentRunStart < 0) {
				currentRunStart = pendingPrefixStart >= 0 ? pendingPrefixStart : index;
				pendingPrefixStart = -1;
				currentScript = script;
			} else if (currentScript is null || currentScript.Value != script) {
				add(items, text, sourceStart, currentRunStart, index - currentRunStart, direction, currentScript, languageBcp47);
				currentRunStart = index;
				currentScript = script;
			}
			index += runeLength;
		}
		int finalStart = Math.Max(currentRunStart, 0);
		add(
			items,
			text,
			sourceStart,
			finalStart,
			text.Length - finalStart,
			direction,
			currentRunStart >= 0 ? currentScript : Script.Common,
			languageBcp47
		);
		return items.ToArray();
	}

	private static bool isRtlLevel(sbyte level) => (level & 1) != 0;
}
