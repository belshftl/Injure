// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Diagnostics;

internal static class Core {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor MissingOpenClassMarker = new(
		id: "IJ0001",
		title: "Every class should be sealed or explicitly marked open",
		messageFormat: "For most classes, you should use `sealed class` and not plain `class`; if you do actually want a non-sealed class, mark it with `/* open */` (for example, `public /* open */ class MyClass`)",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor OpenClassMarkerNotAllowed = new(
		id: "IJ0002",
		title: "Open class marker not allowed here",
		messageFormat: "The `/* open */` marker is only allowed on non-{static/abstract/sealed} classes or non-{abstract/sealed} records",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MalformedOpenClassMarker = new(
		id: "IJ0003",
		title: "Open class marker has invalid placement",
		messageFormat: "The `/* open */` marker must occur exactly once before the declaration keyword and be separated by horizontal whitespace",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor DontImplementInterface = new(
		id: "IJ0004",
		title: "Interface should not be implemented",
		messageFormat: "Interface '{0}' exists solely as an API surface and should not be implemented",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor InvalidMethodAttributeUsageTarget = new(
		id: "IJ0005",
		title: "Invalid [MethodAttributeUsage] target",
		messageFormat: "'{0}' has [MethodAttributeUsage] but does not derive from System.Attribute",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ContradictoryMethodAttributeUsageConstraints = new(
		id: "IJ0006",
		title: "Contradictory [MethodAttributeUsage] constraints",
		messageFormat: "'{0}' declares contradictory method constraints: {1}",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MethodAttributeUsageConstraintViolation = new(
		id: "IJ0007",
		title: "Method does not satisfy attribute constraints",
		messageFormat: "Attribute '{0}' requires the target method to satisfy: {1}",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
