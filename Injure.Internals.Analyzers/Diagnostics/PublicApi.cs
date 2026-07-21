// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Internals.Analyzers.Diagnostics;

internal static class PublicApi {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor PublicForeignType = new(
		id: "IJDEV0100",
		title: "Foreign type in public API",
		messageFormat: "Public API '{0}' exposes type '{1}' from non-BCL/Injure assembly '{2}'; if this is intentionally a foreign type, consider making it a DangerousGet* method",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor PublicEvent = new(
		id: "IJDEV0101",
		title: "Event in public API",
		messageFormat: "Don't expose events as a public API surface or require users to declare events; use [TODO] instead for the simple user-subscribes-to-engine-callback case",
		category: "Language",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
