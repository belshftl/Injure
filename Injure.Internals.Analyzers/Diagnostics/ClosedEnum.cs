// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Internals.Analyzers.Diagnostics;

internal static class ClosedEnum {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor ClosedEnumInvalidTarget = new(
		id: "IJDEV1000",
		title: "Invalid target for attribute ClosedEnum",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMustBeReadonly = new(
		id: "IJDEV1001",
		title: "ClosedEnum target must be readonly",
		messageFormat: "ClosedEnum target struct '{0}' must be a readonly struct",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumInvalidSourceShape = new(
		id: "IJDEV1002",
		title: "Invalid ClosedEnum source shape",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumInvalidCaseEnum = new(
		id: "IJDEV1003",
		title: "Invalid ClosedEnum Case enum",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumAliasNotSupported = new(
		id: "IJDEV1004",
		title: "ClosedEnum aliases are not supported",
		messageFormat: "ClosedEnum Case member '{0}' has the same numeric value as '{1}' ({2}); aliases are not supported",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumDefaultRule = new(
		id: "IJDEV1005",
		title: "ClosedEnum default-value rule violation",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumSuspiciousZeroName = new(
		id: "IJDEV1006",
		title: "ClosedEnum member with zero value does not look neutral",
		messageFormat: "ClosedEnum zero-valued member '{0}' does not look like a neutral/default state; consider renaming it or using DefaultIsInvalid = true",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMirrorInvalid = new(
		id: "IJDEV1007",
		title: "Invalid ClosedEnum mirror declaration",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMirrorMismatch = new(
		id: "IJDEV1008",
		title: "ClosedEnum mirror numeric values do not match",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
