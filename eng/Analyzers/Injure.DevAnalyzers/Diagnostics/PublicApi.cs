// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Diagnostics;

internal static class PublicApi {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor PublicForeignType = new(
		id: "IJDEV0100",
		title: "Foreign type in public API",
		messageFormat: "Public API '{0}' exposes type '{1}' from non-BCL/Injure assembly '{2}'; if this is intentionally a foreign type, consider exposing it through a DangerousGet* method (return type) or DangerousCreate* method (parameter types)",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
