// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Diagnostics;

internal static class Language {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor MissingOpenClassMarker = new(
		id: "IJDEV0001",
		title: "Every class should be sealed or explicitly marked open",
		messageFormat:
		"This should probably be a `sealed class`. Think long and hard before making a class inheritable; if you're really absolutely sure that deriving it is a fully supported public API, mark it with `/* open */` where `sealed` normally goes, and add a doc comment about derived-class behavior/notes/intentions.",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor OpenClassMarkerNotAllowed = new(
		id: "IJDEV0002",
		title: "Open class marker not allowed here",
		messageFormat: "The `/* open */` marker is only allowed on non-{{static/abstract/sealed}} classes or non-{{abstract/sealed}} records",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MalformedOpenClassMarker = new(
		id: "IJDEV0003",
		title: "Open class marker has invalid placement",
		messageFormat: "The `/* open */` marker must occur exactly once before the declaration keyword and be separated by horizontal whitespace",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ForeachImplicitBadCast = new(
		id: "IJDEV0004",
		title: "Iteration variable type mismatch in foreach",
		messageFormat: "Cannot assign to '{0}' from iterator yielding '{1}'",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
