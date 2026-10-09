// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.DevAnalyzers.Diagnostics;

internal static class ColorType {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor ColorTypeInvalidTarget = new(
		id: "IJDEV1100",
		title: "Invalid target for a color type attribute",
		messageFormat: "{0}",
		category: "ColorType",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ColorTypeMustBeReadonly = new(
		id: "IJDEV1101",
		title: "Color type target must be readonly",
		messageFormat: "Color type target struct '{0}' must be a readonly struct",
		category: "ColorType",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ColorTypeMemberCollision = new(
		id: "IJDEV1102",
		title: "Existing member collides with a generated color type member",
		messageFormat: "Type '{0}' declares member '{1}', which the color type generator also emits",
		category: "ColorType",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
