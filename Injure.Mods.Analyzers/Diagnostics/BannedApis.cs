// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Diagnostics;

internal static class BannedApis {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor MonoModUsed = new(
		id: "IJM0300",
		title: "Don't use MonoMod",
		messageFormat: "Use Injure's built-in patch/hook APIs instead of MonoMod; mixing multiple patching APIs causes conflicts, and MonoMod is dated and poor on ordering policy and reload safety",
		category: "BannedApis",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor HarmonyUsed = new(
		id: "IJM0301",
		title: "Don't use Harmony",
		messageFormat: "Use Injure's built-in patch/hook APIs instead of Harmony; mixing multiple patching APIs causes conflicts",
		category: "BannedApis",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
