// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Diagnostics;

internal static class Documentation {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor StructDefaultValueUndocumented = new(
		id: "IJDEV0200",
		title: "Struct does not document its default value",
		messageFormat:
		"Struct '{0}' {1}; the last paragraph of its <remarks> must start with either 'The <see langword=\"default\"/> value is invalid.' or 'The <see langword=\"default\"/> value is valid' followed by what the default value is",
		category: "Documentation",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
