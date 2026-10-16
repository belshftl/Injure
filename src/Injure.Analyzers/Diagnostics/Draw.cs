// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Diagnostics;

internal static class Draw {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor TargetChangedWithoutTransform = new(
		id: "IJ0300",
		title: "Canvas target changed without specifying the transform",
		messageFormat: "This override changes the canvas target without specifying the transform, so the current transform carries over to the new target; set the transform, or make it explicit by passing '{0}: null' if keeping it is intentional",
		category: "Draw",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);
}
