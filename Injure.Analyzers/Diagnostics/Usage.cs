// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Diagnostics;

internal static class Usage {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor DontCacheObject = new(
		id: "IJ0100",
		title: "Object should not be cached",
		messageFormat: "Objects of type '{0}' should not be stored in fields or properties: \"{1}\"",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor DontCaptureObjectIntoClosure = new(
		id: "IJ0101",
		title: "Object should not be captured into a lambda or local function",
		messageFormat: "Objects of type '{0}' should not be captured into lambdas or local functions: \"{1}\"",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor DontImplementInterface = new(
		id: "IJ0102",
		title: "Interface must not be implemented",
		messageFormat: "Interface '{0}' exists solely as an API surface and must not be implemented",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor InvalidMethodAttributeUsageTarget = new(
		id: "IJ0103",
		title: "Invalid [MethodAttributeUsage] target",
		messageFormat: "'{0}' has [MethodAttributeUsage] but does not derive from System.Attribute",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ContradictoryMethodAttributeUsageConstraints = new(
		id: "IJ0104",
		title: "Contradictory [MethodAttributeUsage] constraints",
		messageFormat: "'{0}' declares contradictory method constraints: {1}",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MethodAttributeUsageConstraintViolation = new(
		id: "IJ0105",
		title: "Method does not satisfy attribute constraints",
		messageFormat: "Attribute '{0}' requires the target method to satisfy: {1}",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
