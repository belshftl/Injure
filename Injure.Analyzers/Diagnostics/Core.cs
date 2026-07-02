// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Diagnostics;

internal static class Core {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor DontImplementInterface = new(
		id: "IJ0001",
		title: "Interface should not be implemented",
		messageFormat: "Interface '{0}' exists solely as an API surface and should not be implemented",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor InvalidMethodAttributeUsageTarget = new(
		id: "IJ0002",
		title: "Invalid [MethodAttributeUsage] target",
		messageFormat: "'{0}' has [MethodAttributeUsage] but does not derive from System.Attribute",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ContradictoryMethodAttributeUsageConstraints = new(
		id: "IJ0003",
		title: "Contradictory [MethodAttributeUsage] constraints",
		messageFormat: "'{0}' declares contradictory method constraints: {1}",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MethodAttributeUsageConstraintViolation = new(
		id: "IJ0004",
		title: "Method does not satisfy attribute constraints",
		messageFormat: "Attribute '{0}' requires the target method to satisfy: {1}",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
