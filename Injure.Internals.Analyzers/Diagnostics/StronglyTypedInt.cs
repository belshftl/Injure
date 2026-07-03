// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Internals.Analyzers.Diagnostics;

internal static class StronglyTypedInt {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor StronglyTypedIntInvalidTarget = new(
		id: "IJDEV0200",
		title: "Invalid target for attribute StronglyTypedInt",
		messageFormat: "{0}",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntMustBeReadonly = new(
		id: "IJDEV0201",
		title: "StronglyTypedInt target must be readonly",
		messageFormat: "StronglyTypedInt target struct '{0}' must be a readonly struct",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntUnsupportedBacking = new(
		id: "IJDEV0202",
		title: "Unsupported backing type for StronglyTypedInt",
		messageFormat: "Backing type '{0}' is not supported (supported: sbyte, byte, short, ushort, int, uint, long, ulong, nint, nuint, Int128, UInt128)",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntMemberCollision = new(
		id: "IJDEV0203",
		title: "Existing member collides with reserved member for StronglyTypedInt",
		messageFormat: "{0}",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
