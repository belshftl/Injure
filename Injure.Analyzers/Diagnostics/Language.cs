// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Diagnostics;

internal static class Language {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor MissingOpenClassMarker = new(
		id: "IJ0001",
		title: "Every class should be sealed/abstract or explicitly marked open",
		messageFormat: "For most classes, you should use `sealed class` (or sometimes `abstract class`) instead of plain `class`; if you do genuinely want an instantiatable AND openly inheritable class, mark it with `/* open */` (for example, `public /* open */ class MyClass`)",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor OpenClassMarkerNotAllowed = new(
		id: "IJ0002",
		title: "Open class marker not allowed here",
		messageFormat: "The `/* open */` marker is only allowed on non-{{static/abstract/sealed}} classes or non-{{abstract/sealed}} records",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MalformedOpenClassMarker = new(
		id: "IJ0003",
		title: "Open class marker has invalid placement",
		messageFormat: "The `/* open */` marker must occur exactly once before the declaration keyword and be separated by horizontal whitespace",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ForeachImplicitBadCast = new(
		id: "IJ0004",
		title: "Iteration variable type mismatch in foreach",
		messageFormat: "Cannot assign to '{0}' from iterator yielding '{1}'",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StaticEventDeclared = new(
		id: "IJ0005",
		title: "Static event declared",
		messageFormat:
		"Static events are banned; they're impossible to use correctly from reloadable mods and are generally a code smell. If this is a public API, avoid events altogether and hand out IReloadTeardown handles.",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
