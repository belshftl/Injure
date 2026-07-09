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

	public static readonly DiagnosticDescriptor NonStaticHookMethod = new(
		id: "IJM0201",
		title: "Hook methods should be static methods",
		messageFormat:
		"Hook methods should be static methods; instance methods / capturing lambdas can cause all sorts of chaos by capturing state, and Action/Func/etc objects make it hard to pinpoint what actually gets used as the hook",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor HookStaticLambda = new(
		id: "IJM0202",
		title: "Prefer plain static methods over static lambdas for hooks",
		messageFormat:
		"Prefer a plain static method over a static lambda for hooks; static lambdas make it harder to pinpoint the hook body or give it an identity for debugging/diagnostics/etc",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor NonStaticIlEmitterDelegateMethod = new(
		id: "IJM0203",
		title: "IlEmitter.Delegate argument should be a static method",
		messageFormat:
		"IlEmitter.Delegate should be passed a static method; instance methods / capturing lambdas can cause all sorts of chaos by capturing state, and Action/Func/etc objects make it hard to pinpoint what actually gets called",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor IlEmitterDelegateStaticLambda = new(
		id: "IJM0204",
		title: "Prefer plain static methods over static lambdas for IlEmitter.Delegate",
		messageFormat:
		"Prefer a plain static method over a static lambda for IlEmitter.Delegate, as static lambdas usually emit worse IL; what could be a plain call instruction now has to retain a delegate instance somewhere in internal storage and fetch it at the callsite",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
