// SPDX-License-Identifier: MIT

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Injure.ModKit.Abstractions.ManifestReader;

public static class LocatedJsonParser {
	public static JNode Parse(string sourceName, string text) => Parse(new SourceText(sourceName, text));

	public static JNode Parse(SourceText source) {
		byte[] utf8 = Encoding.UTF8.GetBytes(source.Text);
		int[] byteToCharOffset = buildByteToCharOffsetMap(source.Text, utf8.Length);
		Utf8JsonReader reader = new(
			utf8,
			new JsonReaderOptions {
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip,
			}
		);
		return parseRoot(ref reader, source, utf8, byteToCharOffset);
	}

	internal static int ToCharOffset(SourceText source, long byteOffset, byte[] utf8, int[] byteToCharOffset) {
		int offset = checked((int)Math.Clamp(byteOffset, 0, utf8.Length));
		if (offset >= byteToCharOffset.Length)
			return source.Text.Length;
		return byteToCharOffset[offset];
	}

	private static JNode parseRoot(ref Utf8JsonReader reader, SourceText source, byte[] utf8, int[] byteToCharOffset) {
		try {
			if (!reader.Read())
				throw err("$", JsonSourceSpan.Point(source.GetLocationFromCharOffset(0)), "expected JSON value");
			JNode root = parseValue(ref reader, source, utf8, byteToCharOffset, "$");
			if (reader.Read())
				throw err("$", currentTokenSpan(ref reader, source, utf8, byteToCharOffset), "unexpected token after root JSON value");
			return root;
		} catch (JsonException ex) {
			JsonSourceLocation loc = source.GetLocationFromByteOffset(reader.TokenStartIndex, utf8, byteToCharOffset);
			throw new ManifestReadException("$", JsonSourceSpan.Point(loc), ex.Message);
		}
	}

	private static JNode parseValue(ref Utf8JsonReader reader, SourceText source, byte[] utf8, int[] byteToCharOffset, string path) {
		JsonTokenType token = reader.TokenType;
		return token switch {
			JsonTokenType.StartObject => parseObject(ref reader, source, utf8, byteToCharOffset, path),
			JsonTokenType.StartArray => parseArray(ref reader, source, utf8, byteToCharOffset, path),
			JsonTokenType.String => new JNode {
				Kind = JKind.String,
				NodePath = path,
				Span = currentTokenSpan(ref reader, source, utf8, byteToCharOffset),
				StringValue = reader.GetString(),
			},
			JsonTokenType.Number => new JNode {
				Kind = JKind.Number,
				NodePath = path,
				Span = currentTokenSpan(ref reader, source, utf8, byteToCharOffset),
				NumberText = reader.GetRawString(),
			},
			JsonTokenType.True => new JNode {
				Kind = JKind.Bool,
				NodePath = path,
				Span = currentTokenSpan(ref reader, source, utf8, byteToCharOffset),
				BoolValue = true,
			},
			JsonTokenType.False => new JNode {
				Kind = JKind.Bool,
				NodePath = path,
				Span = currentTokenSpan(ref reader, source, utf8, byteToCharOffset),
				BoolValue = false,
			},
			JsonTokenType.Null => new JNode {
				Kind = JKind.Null,
				NodePath = path,
				Span = currentTokenSpan(ref reader, source, utf8, byteToCharOffset),
			},
			_ => throw err(path, currentTokenSpan(ref reader, source, utf8, byteToCharOffset), $"expected JSON value, got {token}"),
		};
	}

	private static JNode parseObject(ref Utf8JsonReader reader, SourceText source, byte[] utf8, int[] byteToCharOffset, string path) {
		JsonSourceLocation start = source.GetLocationFromByteOffset(reader.TokenStartIndex, utf8, byteToCharOffset);
		Dictionary<string, JProperty> properties = new(StringComparer.Ordinal);

		for (;;) {
			if (!reader.Read())
				throw err(path, JsonSourceSpan.Point(start), "unterminated object");

			if (reader.TokenType == JsonTokenType.EndObject) {
				JsonSourceLocation end = source.GetLocationFromByteOffset(reader.BytesConsumed, utf8, byteToCharOffset);
				return new JNode {
					Kind = JKind.Object,
					NodePath = path,
					Span = new JsonSourceSpan(start, end),
					Properties = properties,
				};
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
				throw err(path, currentTokenSpan(ref reader, source, utf8, byteToCharOffset), $"expected property name, got {reader.TokenType}");

			string name = reader.GetString() ?? "";
			JsonSourceSpan nameSpan = currentTokenSpan(ref reader, source, utf8, byteToCharOffset);
			string propertyPath = appendPropertyPath(path, name);

			if (!reader.Read())
				throw err(propertyPath, nameSpan, "expected property value");

			JNode value = parseValue(ref reader, source, utf8, byteToCharOffset, propertyPath);

			if (properties.ContainsKey(name))
				throw err(propertyPath, nameSpan, $"duplicate property '{name}'");

			properties.Add(name, new JProperty {
				Name = name,
				NameSpan = nameSpan,
				Value = value,
			});
		}
	}

	private static JNode parseArray(ref Utf8JsonReader reader, SourceText source, byte[] utf8, int[] byteToCharOffset, string path) {
		JsonSourceLocation start = source.GetLocationFromByteOffset(reader.TokenStartIndex, utf8, byteToCharOffset);
		List<JNode> items = new();

		for (;;) {
			if (!reader.Read())
				throw err(path, JsonSourceSpan.Point(start), "unterminated array");

			if (reader.TokenType == JsonTokenType.EndArray) {
				JsonSourceLocation end = source.GetLocationFromByteOffset(reader.BytesConsumed, utf8, byteToCharOffset);
				return new JNode {
					Kind = JKind.Array,
					NodePath = path,
					Span = new JsonSourceSpan(start, end),
					Items = items,
				};
			}

			string itemPath = path + "[" + items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
			items.Add(parseValue(ref reader, source, utf8, byteToCharOffset, itemPath));
		}
	}

	private static JsonSourceSpan currentTokenSpan(ref Utf8JsonReader reader, SourceText source, byte[] utf8, int[] byteToCharOffset) {
		int start = ToCharOffset(source, reader.TokenStartIndex, utf8, byteToCharOffset);
		int end = ToCharOffset(source, reader.BytesConsumed, utf8, byteToCharOffset);
		if (end <= start)
			end = Math.Min(source.Text.Length, start + 1);
		return source.SpanFromCharOffsets(start, end);
	}

	private static string appendPropertyPath(string path, string name) {
		if (isSimplePathName(name))
			return path + "." + name;
		return path + "[" + quotePathString(name) + "]";
	}

	private static bool isSimplePathName(string name) {
		if (name.Length == 0)
			return false;
		foreach (char ch in name)
			if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_'))
				return false;
		return true;
	}

	private static string quotePathString(string s) =>
		"\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

	private static int[] buildByteToCharOffsetMap(string text, int byteLength) {
		int[] map = new int[byteLength + 1];
		int byteOffset = 0;

		for (int charOffset = 0; charOffset < text.Length;) {
			Rune rune;
			int charsThisScalar;

			if (char.IsHighSurrogate(text[charOffset]) && charOffset + 1 < text.Length && char.IsLowSurrogate(text[charOffset + 1])) {
				rune = new Rune(text[charOffset], text[charOffset + 1]);
				charsThisScalar = 2;
			} else {
				rune = new Rune(text[charOffset]);
				charsThisScalar = 1;
			}

			int bytes = rune.Utf8SequenceLength;
			for (int i = 0; i < bytes && byteOffset + i < map.Length; i++)
				map[byteOffset + i] = charOffset;

			byteOffset += bytes;
			charOffset += charsThisScalar;
		}

		for (int i = Math.Min(byteOffset, map.Length - 1); i < map.Length; i++)
			map[i] = text.Length;
		return map;
	}

	private static ManifestReadException err(string path, JsonSourceSpan span, string message) => new(path, span, message);
}

internal static class LocatedJsonParserRelatedExtensions {
	extension(ref Utf8JsonReader reader) {
		public string GetRawString() {
			if (reader.HasValueSequence)
				return Encoding.UTF8.GetString(reader.ValueSequence.ToArray());
			return Encoding.UTF8.GetString(reader.ValueSpan);
		}
	}

	extension(SourceText source) {
		public JsonSourceLocation GetLocationFromByteOffset(long byteOffset, byte[] utf8, int[] byteToCharOffset) =>
			source.GetLocationFromCharOffset(LocatedJsonParser.ToCharOffset(source, byteOffset, utf8, byteToCharOffset));
	}
}
