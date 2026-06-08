// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Injure.ModKit.Abstractions.ManifestReader;

public readonly record struct JsonSourceLocation(
	int Line,
	int Column,
	int Offset
);

public readonly record struct JsonSourceSpan(
	JsonSourceLocation Start,
	JsonSourceLocation End
) {
	public static JsonSourceSpan Point(JsonSourceLocation location) => new(location, location);
}

public sealed class SourceText(string sourceName, string text) {
	private readonly string sourceName = sourceName;
	private readonly string text = text;
	private readonly int[] lineStarts = computeLineStarts(text);

	public string SourceName => sourceName;
	public string Text => text;

	public JsonSourceLocation GetLocationFromCharOffset(int offset) {
		offset = Math.Clamp(offset, 0, text.Length);
		int line = Array.BinarySearch(lineStarts, offset);
		if (line < 0)
			line = ~line - 2;
		if (line < 0)
			line = 0;
		return new JsonSourceLocation(
			Line: line + 1,
			Column: offset - lineStarts[line] + 1,
			Offset: offset
		);
	}

	public JsonSourceSpan SpanFromCharOffsets(int start, int end) =>
		new(GetLocationFromCharOffset(start), GetLocationFromCharOffset(Math.Max(start, end)));

	public string FormatDiagnostic(ManifestReadException ex) => FormatDiagnostic(ex.JsonNodePath, ex.Message, ex.Span);

	public string FormatDiagnostic(string jsonNodePath, string message, JsonSourceSpan span) {
		int lineNo = span.Start.Line + 1;
		int lineIdx = Math.Clamp(span.Start.Line, 0, lineStarts.Length - 1);
		int lineStart = lineStarts[lineIdx];
		int lineEnd = getLineEnd(lineIdx);
		string line = text[lineStart..lineEnd];

		int startCol = Math.Max(1, span.Start.Offset - lineStart + 1);
		int endCol = span.End.Line == span.Start.Line ? Math.Max(startCol + 1, span.End.Offset - lineStart + 1) : line.Length + 1;

		string expLine = expandTabs(line, out int[] colToVisual);
		int visualStart = mapCol(colToVisual, startCol);
		int visualEnd = mapCol(colToVisual, endCol);
		int visualLen = Math.Max(1, visualEnd - visualStart);

		string gutter = lineNo.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(4);

		StringBuilder sb = new();
		sb.Append(makePathPrettier(sourceName))
			.Append(':').Append(lineNo)
			.Append(':').Append(span.Start.Column)
			.Append(": error: ")
			.Append(jsonNodePath)
			.Append(": ")
			.AppendLine(message);

		sb.Append(' ').Append(gutter).Append(" | ").AppendLine(expLine);
		sb.Append(' ').Append(new string(' ', gutter.Length)).Append(" | ")
			.Append(new string(' ', Math.Max(0, visualStart - 1)))
			.AppendLine(new string('^', visualLen));

		return sb.ToString();
	}

	private static string makePathPrettier(string path) {
		static bool endsWithSep(string s) =>
			s.Length != 0 && (s[^1] == Path.DirectorySeparatorChar || s[^1] == Path.AltDirectorySeparatorChar);

		static string normalizePrefix(string prefix) {
			prefix = Path.GetFullPath(prefix);
			string? root = Path.GetPathRoot(prefix);
			if (root is not null && prefix.Length > root.Length && endsWithSep(prefix))
				prefix = prefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return prefix;
		}

		static bool TryStripPrefix(string path, string prefix, out string suffix) {
			prefix = normalizePrefix(prefix);
			if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
				suffix = "";
				return false;
			}
			if (path.Length != prefix.Length && !endsWithSep(prefix) && path[prefix.Length] != Path.DirectorySeparatorChar &&
				path[prefix.Length] != Path.AltDirectorySeparatorChar) {
				suffix = "";
				return false;
			}
			suffix = path.Length == prefix.Length ? "" : path[(prefix.Length + (endsWithSep(prefix) ? 0 : 1))..];
			return true;
		}

		path = Path.GetFullPath(path);
		if (TryStripPrefix(path, AppContext.BaseDirectory, out string relToBase))
			return relToBase.Length == 0 ? "." : relToBase;
		if (TryStripPrefix(path, Environment.CurrentDirectory, out string relToCwd))
			return relToCwd.Length == 0 ? "." : "." + Path.DirectorySeparatorChar + relToCwd;
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (home.Length != 0 && TryStripPrefix(path, home, out string relToHome))
			return relToHome.Length == 0 ? "~" : "~" + Path.DirectorySeparatorChar + relToHome;

		return path;
	}

	private int getLineEnd(int lineIndex) {
		int nextStart = lineIndex + 1 < lineStarts.Length ? lineStarts[lineIndex + 1] : text.Length;
		int end = nextStart;
		while (end > lineStarts[lineIndex] && (text[end - 1] == '\n' || text[end - 1] == '\r'))
			end--;
		return end;
	}

	private static int[] computeLineStarts(string text) {
		List<int> starts = new() { 0 };
		for (int i = 0; i < text.Length; i++)
			if (text[i] == '\n') {
				starts.Add(i + 1);
			} else if (text[i] == '\r') {
				if (i + 1 < text.Length && text[i + 1] == '\n')
					i++;
				starts.Add(i + 1);
			}
		return starts.ToArray();
	}

	private static string expandTabs(string line, out int[] colToVisual) {
		colToVisual = new int[line.Length + 2];

		StringBuilder sb = new();
		int visual = 1;
		for (int i = 0; i < line.Length; i++) {
			colToVisual[i + 1] = visual;
			if (line[i] == '\t') {
				int spaces = 8 - (visual - 1) % 8;
				sb.Append(' ', spaces);
				visual += spaces;
			} else {
				sb.Append(line[i]);
				visual++;
			}
		}
		colToVisual[line.Length + 1] = visual;
		return sb.ToString();
	}

	private static int mapCol(int[] colToVisual, int col) {
		if (col <= 1)
			return 1;
		if (col >= colToVisual.Length)
			return colToVisual[^1];
		return colToVisual[col];
	}
}
