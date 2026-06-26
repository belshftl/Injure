// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Diagnostics;

internal static class Core {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor MissingModAssemblyAttribute = new(
		id: "IJM0001",
		title: "[ModAssembly] attribute missing",
		messageFormat: "Mod assemblies must declare a [ModAssembly] attribute",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor BadHotReloadLevel = new(
		id: "IJM0002",
		title: "Invalid [ModAssembly] hot reload level",
		messageFormat: "[ModAssembly] hot reload level value '{0}' is not a named ModAssemblyHotReloadLevel value",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor NullLifetimeIdentityTypeArgument = new(
		id: "IJM0003",
		title: "Null [ModAssembly] lifetime identity type argument",
		messageFormat: "[ModAssembly] lifetime identity type argument is null",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor BadLifetimeIdentityType = new(
		id: "IJM0004",
		title: "Invalid mod lifetime identity marker type",
		messageFormat:
		"Mod lifetime identity type '{0}' must be a public non-generic/nested/byreflike empty readonly struct implementing IModLifetimeIdentity, marked with [ModLifetimeIdentityBelongsTo(...)]",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor LifetimeIdentityOwnerMismatch = new(
		id: "IJM0005",
		title: "Mod lifetime identity marker says it belongs to a different owner",
		messageFormat: "Mod lifetime identity type '{0}' says it belongs to owner '{1}', while the mod assembly is '{2}'",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MissingEntrypoint = new(
		id: "IJM0006",
		title: "Mod entrypoint missing",
		messageFormat: "Mods must declare a [ModEntrypoint] closed-generic sealed class implementing IModEntrypoint<in TGameApi, L>",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor BadEntrypointTarget = new(
		id: "IJM0007",
		title: "Invalid [ModEntrypoint] class",
		messageFormat: "[ModEntrypoint] target class '{0}' must be a closed-generic sealed class implementing IModEntrypoint<in TGameApi, L> with lifetime marker '{1}'",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor DuplicateEntrypoint = new(
		id: "IJM0008",
		title: "Duplicate [ModEntrypoint] classes",
		messageFormat: "There must be exactly one [ModEntrypoint] in the entire assembly; found {0}",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MissingReloadEntrypoint = new(
		id: "IJM0009",
		title: "Mod reload entrypoint missing",
		messageFormat: "Live-reloadable mods must declare a [ModReloadEntrypoint] closed-generic sealed class implementing IModReloadEntrypoint<in TGameApi, L>",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor BadReloadEntrypointTarget = new(
		id: "IJM0010",
		title: "Invalid [ModReloadEntrypoint] class",
		messageFormat:
		"[ModReloadEntrypoint] target class '{0}' must be a closed-generic sealed class implementing IModReloadEntrypoint<in TGameApi, L> with TGameApi '{1}' and lifetime marker '{2}'",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor DuplicateReloadEntrypoint = new(
		id: "IJM0011",
		title: "Duplicate [ModReloadEntrypoint] classes",
		messageFormat: "There must be exactly one [ModReloadEntrypoint] in the entire assembly; found {0}",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor DisallowedReloadEntrypoint = new(
		id: "IJM0012",
		title: "Only live-reloadable mods may have a reload entrypoint",
		messageFormat: "A [ModReloadEntrypoint] is only allowed for mods whose hot reload level is Live",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor LifetimeIdentityInterfaceAsValue = new(
		id: "IJM0013",
		title: "IModLifetimeIdentity used as an ordinary type",
		messageFormat: "'{0}' is a lifetime identity marker and must not be used as an ordinary value/member type",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor LifetimeIdentityTypeParamAsValue = new(
		id: "IJM0014",
		title: "Lifetime identity type parameter used as an ordinary type",
		messageFormat: "Type parameter '{0}' is a lifetime identity marker and must not be used as an ordinary value/member type",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MissingStructLifetimeConstraint = new(
		id: "IJM0015",
		title: "Lifetime identity type parameter can be constrained to struct",
		messageFormat: "Add 'struct' to the IModLifetimeIdentity constraint for type parameter '{0}'",
		category: "Core",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
