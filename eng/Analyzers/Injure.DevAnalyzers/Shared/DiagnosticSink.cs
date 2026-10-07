// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Shared;

// lets the analyzer and the generator of a feature share the same validation logic; the generator
// uses a silent one and only checks HasErrors
internal sealed class DiagnosticSink(Action<Diagnostic>? report) {
	public bool HasErrors { get; private set; }

	public static DiagnosticSink Silent() => new(null);

	public void Report(DiagnosticDescriptor descriptor, Location loc, params object?[] args) {
		if (descriptor.DefaultSeverity == DiagnosticSeverity.Error)
			HasErrors = true;
		report?.Invoke(Diagnostic.Create(descriptor, loc, args));
	}
}
