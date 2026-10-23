// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using HarfBuzzSharp;

namespace Injure.Draw.Text;

/// <summary>
/// Bindings for <c>ijtext</c>; see <c>native/text/include/abi.h</c>.
/// </summary>
internal static unsafe partial class IjText {
	public enum IjtResult : uint {
		Ok = 0,
		InvalidArgument = 1,
	}

	public enum BreakKind : byte {
		None = 0,
		Soft = 1,
		Hard = 2,
	}

	/// <remarks>
	/// The <see langword="default"/> value is valid and is an empty LTR run at 0.
	/// </remarks>
	[StructLayout(LayoutKind.Sequential)]
	public struct BidiRun {
		public uint Start;
		public uint Len;
		public uint Rtl;
	}

	private const string library = "ijtext";

#if DEBUG
	static IjText() {
		if (Unsafe.SizeOf<BidiRun>() != 12)
			throw new InternalStateException("expected BidiRun size to be 12 bytes");
		// c# has no alignof; skip alignment assertion
		if (Marshal.OffsetOf<BidiRun>(nameof(BidiRun.Start)) != 0)
			throw new InternalStateException("expected BidiRun Start offset to be 0");
		if (Marshal.OffsetOf<BidiRun>(nameof(BidiRun.Len)) != 4)
			throw new InternalStateException("expected BidiRun Len offset to be 4");
		if (Marshal.OffsetOf<BidiRun>(nameof(BidiRun.Rtl)) != 8)
			throw new InternalStateException("expected BidiRun Rtl offset to be 8");
	}
#endif

	[LibraryImport(library)]
	private static partial IjtResult ijt_line_breaks(char* text, nuint len, byte* language, nuint languageLen, BreakKind* kinds);

	[LibraryImport(library)]
	private static partial IjtResult ijt_bidi_open(char* text, nuint len, nint* @out);

	[LibraryImport(library)]
	private static partial IjtResult ijt_bidi_levels(nint bidi, byte* levels, nuint len);

	[LibraryImport(library)]
	private static partial IjtResult ijt_bidi_visual_runs(nint bidi, nuint lineStart, nuint lineLimit, BidiRun* runs, nuint cap, nuint* count);

	[LibraryImport(library)]
	public static partial void ijt_bidi_close(nint bidi);

	private static void check(IjtResult result, string function) {
		if (result != 0)
			throw new InternalStateException($"{function} failed with result {result}");
	}

	public static void LineBreaks(ReadOnlySpan<char> text, string language, Span<BreakKind> kinds) {
		if (kinds.Length != text.Length + 1)
			throw new InternalStateException("line break kinds buffer has the wrong length");
		int bytes = Encoding.UTF8.GetMaxByteCount(language.Length);
		Span<byte> lang = bytes <= 2048 ? stackalloc byte[bytes] : new byte[bytes];
		int langLen = Encoding.UTF8.GetBytes(language, lang);
		fixed (char* pText = text)
		fixed (byte* pLang = lang)
		fixed (BreakKind* pKinds = kinds)
			check(ijt_line_breaks(pText, (nuint)text.Length, pLang, (nuint)langLen, pKinds), nameof(ijt_line_breaks));
	}

	public static nint BidiOpen(ReadOnlySpan<char> text) {
		nint bidi;
		fixed (char* pText = text)
			check(ijt_bidi_open(pText, (nuint)text.Length, &bidi), nameof(ijt_bidi_open));
		return bidi;
	}

	public static void BidiLevels(nint bidi, Span<byte> levels) {
		fixed (byte* pLevels = levels)
			check(ijt_bidi_levels(bidi, pLevels, (nuint)levels.Length), nameof(ijt_bidi_levels));
	}

	public static int BidiVisualRuns(nint bidi, int lineStart, int lineLimit, Span<BidiRun> runs) {
		if (runs.Length < lineLimit - lineStart)
			throw new InternalStateException("bidi runs buffer too small");
		nuint count;
		fixed (BidiRun* pRuns = runs)
			check(ijt_bidi_visual_runs(bidi, (nuint)lineStart, (nuint)lineLimit, pRuns, (nuint)runs.Length, &count), nameof(ijt_bidi_visual_runs));
		return (int)count;
	}
}

/// <remarks>
/// The <see langword="default"/> value is valid and is an empty span starting at 0.
/// </remarks>
internal readonly record struct GraphemeSpan(
	int Start,
	int Length
);

internal static class TextAnalysis {
	/// <summary>
	/// A paragraph analyzed by the Unicode Bidirectional Algorithm, whose direction comes from its
	/// first strong character (left-to-right if it has none). Positions are UTF-16 code units.
	/// </summary>
	public sealed class ParagraphAnalysis : IDisposable {
		private nint handle;
		private readonly byte[] levels;

		private ParagraphAnalysis(string text, nint handle, byte[] levels) {
			Text = text;
			this.handle = handle;
			this.levels = levels;
		}

		public string Text { get; }

		public static ParagraphAnalysis Create(string text) {
			nint handle = IjText.BidiOpen(text);
			try {
				var levels = new byte[text.Length];
				IjText.BidiLevels(handle, levels);
				return new ParagraphAnalysis(text, handle, levels);
			} catch {
				IjText.ijt_bidi_close(handle);
				throw;
			}
		}

		private nint h => handle != 0 ? handle : throw new InternalStateException("ParagraphAnalysis already disposed");

		/// <summary>
		/// Adds the maximal runs of one direction, in logical order.
		/// </summary>
		public void GetLogicalRuns(List<LogicalBidiRun> dst) {
			_ = h;
			int start = 0;
			while (start < levels.Length) {
				bool rtl = isRtlLevel(levels[start]);
				int limit = start + 1;
				while (limit < levels.Length && isRtlLevel(levels[limit]) == rtl)
					limit++;
				dst.Add(new LogicalBidiRun(start, limit - start, rtl ? Direction.RightToLeft : Direction.LeftToRight));
				start = limit;
			}
		}

		/// <summary>
		/// Adds the runs of a line in visual order, left to right.
		/// </summary>
		public void GetVisualRunsForLine(List<VisualBidiRun> dst, int lineStart, int lineLimit) {
			nint para = h;
			int length = lineLimit - lineStart;
			if (length <= 0)
				return;
			IjText.BidiRun[] runs = ArrayPool<IjText.BidiRun>.Shared.Rent(length);
			try {
				int count = IjText.BidiVisualRuns(para, lineStart, lineLimit, runs.AsSpan(0, length));
				foreach (IjText.BidiRun run in runs.AsSpan(0, count)) {
					dst.Add(new VisualBidiRun(
						(int)run.Start,
						(int)run.Len,
						run.Rtl != 0 ? Direction.RightToLeft : Direction.LeftToRight
					));
				}
			} finally {
				ArrayPool<IjText.BidiRun>.Shared.Return(runs);
			}
		}

		public void Dispose() {
			if (handle == 0)
				return;
			IjText.ijt_bidi_close(handle);
			handle = 0;
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
	/// Gets the positions after which a line may or must break, for text in the language
	/// <paramref name="language"/> (a BCP 47 tag; see <see cref="TextStyle.Language"/>).
	/// </summary>
	public static LineBreakOpportunity[] GetLineBreaks(string text, string language) {
		if (text.Length == 0)
			return [];
		Span<IjText.BreakKind> kinds = text.Length < 2048
			? stackalloc IjText.BreakKind[text.Length + 1]
			: new IjText.BreakKind[text.Length + 1];
		IjText.LineBreaks(text, language, kinds);
		List<LineBreakOpportunity> breaks = new();
		// XXX: update this if IjText.BreakKind and LineBreakKind ever diverge in numeric values
		for (int i = 1; i < kinds.Length; i++)
			if (kinds[i] != IjText.BreakKind.None)
				breaks.Add(new LineBreakOpportunity(i, (LineBreakKind)kinds[i]));
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

	private static bool isRtlLevel(byte level) => (level & 1) != 0;
}
