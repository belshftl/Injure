// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Internals.Analyzers.Shared;

internal static class Diagnostics {
	// IJI0001-IJI0099 infrastructure/shared
	// IJI0100-IJI0124 ClosedEnum
	// IJI0125-IJI0149 ClosedFlags
	// IJI0200-IJI0249 StronglyTypedInt
	// TODO: also figure out subranges which also probably means renumbering them Again

#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor ClosedEnumInvalidTarget = new(
		id: "IJI0101",
		title: "invalid target for attribute ClosedEnum",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMustBeReadonly = new(
		id: "IJI0102",
		title: "ClosedEnum target must be readonly",
		messageFormat: "ClosedEnum target struct '{0}' must be a readonly struct",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumInvalidSourceShape = new(
		id: "IJI0103",
		title: "invalid ClosedEnum source shape",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumInvalidCaseEnum = new(
		id: "IJI0104",
		title: "invalid ClosedEnum Case enum",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumAliasNotSupported = new(
		id: "IJI0105",
		title: "ClosedEnum aliases are not supported",
		messageFormat: "ClosedEnum Case member '{0}' has the same numeric value as '{1}' ({2}); aliases are not supported",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumDefaultRule = new(
		id: "IJI0106",
		title: "ClosedEnum default-value rule violation",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumSuspiciousZeroName = new(
		id: "IJI0107",
		title: "ClosedEnum member with zero value does not look neutral",
		messageFormat: "ClosedEnum zero-valued member '{0}' does not look like a neutral/default state; consider renaming it or using DefaultIsInvalid = true",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMirrorInvalid = new(
		id: "IJI0108",
		title: "invalid ClosedEnum mirror declaration",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMirrorMismatch = new(
		id: "IJI0109",
		title: "ClosedEnum mirror numeric values do not match",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsInvalidTarget = new(
		id: "IJI0125",
		title: "invalid target for attribute ClosedFlags",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMustBeReadonly = new(
		id: "IJI0126",
		title: "ClosedFlags target must be readonly",
		messageFormat: "ClosedFlags target struct '{0}' must be a readonly struct",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsInvalidSourceShape = new(
		id: "IJI0127",
		title: "invalid ClosedFlags source shape",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsInvalidBitsEnum = new(
		id: "IJI0128",
		title: "invalid ClosedFlags Bits enum",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsAliasNotSupported = new(
		id: "IJI0129",
		title: "ClosedFlags aliases are not supported",
		messageFormat: "ClosedFlags Bits member '{0}' has the same numeric value as '{1}' ({2}); aliases are not supported",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsBadMemberValue = new(
		id: "IJI0130",
		title: "ClosedFlags members must all be atomic powers of two or ORs of previously declared members",
		messageFormat: "ClosedFlags Bits member '{0}' is not a power of two and does not consist of purely already known power-of-two members",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsDefaultRule = new(
		id: "IJI0131",
		title: "ClosedFlags default-value rule violation",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsSuspiciousZeroName = new(
		id: "IJI0132",
		title: "ClosedFlags member with zero value does not look neutral",
		messageFormat: "ClosedFlags zero-valued member '{0}' does not look like a neutral/default state; consider renaming it or using DefaultIsInvalid = true",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMirrorInvalid = new(
		id: "IJI0133",
		title: "invalid ClosedFlags mirror declaration",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMirrorMismatch = new(
		id: "IJI0134",
		title: "ClosedFlags mirror numeric values do not match",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntInvalidTarget = new(
		id: "IJI0201",
		title: "invalid target for attribute StronglyTypedInt",
		messageFormat: "{0}",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntMustBeReadonly = new(
		id: "IJI0202",
		title: "StronglyTypedInt target must be readonly",
		messageFormat: "StronglyTypedInt target struct '{0}' must be a readonly struct",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntUnsupportedBacking = new(
		id: "IJI0203",
		title: "unsupported backing type for StronglyTypedInt",
		messageFormat: "backing type '{0}' is not supported (supported: int, uint, long, ulong, Int128, UInt128)",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntMemberCollision = new(
		id: "IJI0204",
		title: "existing member collides with reserved member for StronglyTypedInt",
		messageFormat: "{0}",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
