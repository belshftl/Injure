// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Diagnostics;

internal static class WrapperType {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor WrapperTypeInvalidTarget = new(
		id: "IJDEV1075",
		title: "Invalid target for attribute WrapperType",
		messageFormat: "{0}",
		category: "WrapperType",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor WrapperTypeMustBeReadonly = new(
		id: "IJDEV1076",
		title: "WrapperType target struct must be readonly",
		messageFormat: "WrapperType target struct '{0}' must be a readonly struct",
		category: "WrapperType",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor WrapperTypeInvalidSourceShape = new(
		id: "IJDEV1077",
		title: "Invalid WrapperType source shape",
		messageFormat: "{0}",
		category: "WrapperType",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor WrapperTypeInvalidWrappedType = new(
		id: "IJDEV1078",
		title: "Invalid WrapperType wrapped type",
		messageFormat: "{0}",
		category: "WrapperType",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor WrapperTypeInvalidMember = new(
		id: "IJDEV1079",
		title: "Invalid WrapperType forwarded member",
		messageFormat: "{0}",
		category: "WrapperType",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
