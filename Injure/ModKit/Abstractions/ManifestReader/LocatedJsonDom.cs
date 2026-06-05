// SPDX-License-Identifier: MIT

using System.Collections.Generic;

using Injure.Internals.Analyzers.Attributes;

namespace Injure.ModKit.Abstractions.ManifestReader;

[ClosedEnum]
public readonly partial struct JKind {
	public enum Case {
		Null,
		Bool,
		String,
		Number,
		Object,
		Array,
	}
}

public sealed class JProperty {
	public required string Name { get; init; }
	public required JsonSourceSpan NameSpan { get; init; }
	public required JNode Value { get; init; }
}

public sealed class JNode {
	public required JKind Kind { get; init; }
	public required string NodePath { get; init; }
	public required JsonSourceSpan Span { get; init; }

	public string? StringValue { get; init; }
	public bool BoolValue { get; init; }
	public string? NumberText { get; init; }

	public IReadOnlyDictionary<string, JProperty>? Properties { get; init; }
	public IReadOnlyList<JNode>? Items { get; init; }
}
