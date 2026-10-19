// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.ManifestReader;

public sealed class ManifestReadException(string path, JsonSourceSpan span, string message) : Exception(message) {
	public string JsonNodePath { get; } = path;
	public JsonSourceSpan Span { get; } = chk(span);

	private static JsonSourceSpan chk(JsonSourceSpan span) {
		ArgumentInvalidDefaultException.ThrowIfDefault(span);
		return span;
	}
}
