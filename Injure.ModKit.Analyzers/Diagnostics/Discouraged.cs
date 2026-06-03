// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Diagnostics;

internal static class Discouraged {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor StaticEventInNonReloadableMod = new(
		id: "IJM0200",
		title: "Static event in non-reloadable mod",
		messageFormat: "Static events are arbitrary, possibly cross-ALC registration buckets with no mechanism to force unregistry and can easily leak old generations of reloadable mods; they're allowed in non-reloadable mods, but strongly prefer a IActiveOwnerScope-managed object instead",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StaticEventInReloadableMod = new(
		id: "IJM0201",
		title: "Static event in reloadable mod",
		messageFormat: "Static events are impossible to use correctly in reloadable mods; registrations easily leak old generation state of other mods, state cannot survive reloads, and they promote bad APIs relying on fragile reflection tracking that falls apart for instance methods or capturing lambdas",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor HookFieldInReloadableMod = new(
		id: "IJM0202",
		title: "Hook field/property in reloadable mod",
		messageFormat: "Don't store hooks in fields/properties in reloadable mods; keep hooks scoped to either the entire generation or the same method that creates it, and prefer built-in hook APIs. For conditional behavior, keep the hook installed and branch inside the hook.",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor LifecycleContextMember = new(
		id: "IJM0203",
		title: "Lifecycle context stored in field/property",
		messageFormat: "Don't store lifecycle context objects as they become invalid after the call returns; store specific long-lived values such as Api / Scope / Diagnostics",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor LifecycleContextLambdaCapture = new(
		id: "IJM0204",
		title: "Lifecycle context captured by lambda",
		messageFormat: "Lifecycle context objects become invalid after the call returns so this lambda capture will most likely not work how you think it will",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor NonStaticHookMethod = new(
		id: "IJM0205",
		title: "Hook methods should be plain static methods",
		messageFormat: "Hook methods should be plain static methods; instance methods / capturing lambdas can cause all sorts of chaos by capturing state, and Action/Func/etc objects make it hard to pinpoint what actually gets used as the hook",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor HookStaticLambda = new(
		id: "IJM0206",
		title: "Prefer plain static methods over static lambdas for hooks",
		messageFormat: "Prefer a plain static method over a static lambda for hooks; static lambdas make it harder to pinpoint the hook body or give it an identity and make debugging/diagnostics/etc worse",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor NonStaticEmitDelegateMethod = new(
		id: "IJM0207",
		title: "EmitDelegate argument should be a plain static methods",
		messageFormat: "EmitDelegate should be passed a plain static method; instance methods / capturing lambdas can cause all sorts of chaos by capturing state, and Action/Func/etc objects make it hard to pinpoint what actually gets called",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor EmitDelegateStaticLambda = new(
		id: "IJM0208",
		title: "Prefer plain static methods over static lambdas for EmitDelegate",
		messageFormat: "Prefer a plain static method over a static lambda for EmitDelegate, as static lambdas usually emit worse IL; what could be a plain call instruction becomes a capture of a delegate field on a compiler-generated class, and the callsite has to do weird castclass magic",
		category: "Discouraged",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
