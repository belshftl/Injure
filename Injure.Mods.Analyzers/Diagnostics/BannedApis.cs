// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Diagnostics;

internal static class BannedApis {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor MonoModAssemblyReferenced = new(
		id: "IJM0300",
		title: "MonoMod assembly referenced",
		messageFormat:
		"Assembly reference '{0}' is not allowed; use Injure's built-in detour/patch APIs instead of MonoMod, as mixing multiple patching APIs causes conflicts, and MonoMod is dated and poor on ordering policy / reload safety",
		category: "BannedApis",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		customTags: [WellKnownDiagnosticTags.CompilationEnd]
	);

	public static readonly DiagnosticDescriptor MonoModUsed = new(
		id: "IJM0301",
		title: "MonoMod used",
		messageFormat:
		"Use Injure's built-in detour/patch APIs instead of MonoMod; mixing multiple patching APIs causes conflicts, and MonoMod is dated and poor on ordering policy / reload safety",
		category: "BannedApis",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor HarmonyAssemblyReferenced = new(
		id: "IJM0302",
		title: "Harmony assembly referenced",
		messageFormat: "Assembly reference '{0}' is not allowed; use Injure's built-in detour/patch APIs instead of Harmony, as mixing multiple patching APIs causes conflicts",
		category: "BannedApis",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		customTags: [WellKnownDiagnosticTags.CompilationEnd]
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
