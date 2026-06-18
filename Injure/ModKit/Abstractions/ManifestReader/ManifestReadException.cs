// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions.ManifestReader;

public sealed class ManifestReadException(string path, JsonSourceSpan span, string message) : Exception(message) {
	public string JsonNodePath { get; } = path;
	public JsonSourceSpan Span { get; } = span;
}
