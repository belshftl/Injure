// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Diagnostics;

internal static class Discouraged {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor StaticEventSubscriptionInReloadableMod = new(
		id: "IJM0200",
		title: "Static event subscription in reloadable mod",
		messageFormat:
		"Static events are impossible to use correctly in reloadable mods; switch to another API, and if you're forced to stay on this one, you may have to make your mod non-reloadable",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
