// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Diagnostics;

internal static class Usage {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor InvalidDefaultCreated = new(
		id: "IJDEV0100",
		title: "Invalid default value created",
		messageFormat: "'{0}' is a default-is-invalid struct",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor InvalidDefaultOptionalParameter = new(
		id: "IJDEV0101",
		title: "Optional parameter defaults to an invalid default value",
		messageFormat: "Parameter '{0}' defaults to the default value of '{1}', which is a default-is-invalid struct",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor InvalidDefaultFieldInStruct = new(
		id: "IJDEV0102",
		title: "Default-value invalidity is transitive",
		messageFormat: "'{0}' is a default-is-invalid struct '{1}', so '{2}' must be one too",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor InterfaceImplKindViolation = new(
		id: "IJDEV0109",
		title: "Interface implemented by the wrong kind of type",
		messageFormat: "Interface '{0}' can only be implemented by {1}",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
