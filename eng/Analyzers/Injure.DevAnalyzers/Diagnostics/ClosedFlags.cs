// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Diagnostics;

internal static class ClosedFlags {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor ClosedFlagsInvalidTarget = new(
		id: "IJDEV1025",
		title: "Invalid target for attribute ClosedFlags",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMustBeReadonly = new(
		id: "IJDEV1026",
		title: "ClosedFlags target must be readonly",
		messageFormat: "ClosedFlags target struct '{0}' must be a readonly struct",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsInvalidSourceShape = new(
		id: "IJDEV1027",
		title: "Invalid ClosedFlags source shape",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsInvalidBitsEnum = new(
		id: "IJDEV1028",
		title: "Invalid ClosedFlags Bits enum",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsAliasNotSupported = new(
		id: "IJDEV1029",
		title: "ClosedFlags aliases are not supported",
		messageFormat: "ClosedFlags Bits member '{0}' has the same numeric value as '{1}' ({2}); aliases are not supported",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsBadMemberValue = new(
		id: "IJDEV1030",
		title: "ClosedFlags members must all be atomic powers of two or ORs of previously declared members",
		messageFormat: "ClosedFlags Bits member '{0}' is not a power of two and does not consist of purely already known power-of-two members",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsDefaultRule = new(
		id: "IJDEV1031",
		title: "ClosedFlags default-value rule violation",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsSuspiciousZeroName = new(
		id: "IJDEV1032",
		title: "ClosedFlags member with zero value does not look neutral",
		messageFormat: "ClosedFlags zero-valued member '{0}' does not look like a neutral/default state; consider renaming it or using DefaultIsInvalid = true",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMirrorInvalid = new(
		id: "IJDEV1033",
		title: "Invalid ClosedFlags mirror declaration",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMirrorMismatch = new(
		id: "IJDEV1034",
		title: "ClosedFlags mirror numeric values do not match",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
