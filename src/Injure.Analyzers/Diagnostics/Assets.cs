// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Diagnostics;

internal static class Assets {
#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor CatalogHasNoFiles = new(
		id: "IJ0200",
		title: "Asset catalog directory has no additional files",
		messageFormat: "No additional files are in '{0}', so the asset catalog is empty; add <AdditionalFiles Include=\"{0}/**\" /> to the project",
		category: "Assets",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor CatalogNameCollision = new(
		id: "IJ0201",
		title: "Asset catalog names collide",
		messageFormat: "In '{0}', {1} all map to the name '{2}', so they're left out of the catalog; rename all but one",
		category: "Assets",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor CatalogUnmappableName = new(
		id: "IJ0202",
		title: "Asset file or directory name can't be turned into a C# name",
		messageFormat: "'{0}' has no letters or digits to make a C# name from, so it's left out of the asset catalog",
		category: "Assets",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor CatalogUnknownKinds = new(
		id: "IJ0203",
		title: "Asset files of unknown kinds",
		messageFormat: "{0} file(s) in '{1}' have no known asset kind and are left out of the catalog (add [assembly: AssetFileKind] for them): {2}",
		category: "Assets",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor CatalogNoProjectDir = new(
		id: "IJ0204",
		title: "Project directory isn't visible to the asset catalog generator",
		messageFormat: "The asset catalog generator needs the ProjectDir build property; the Injure.Analyzers package normally provides it, otherwise add <CompilerVisibleProperty Include=\"ProjectDir\" /> to the project",
		category: "Assets",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor CatalogInvalidTarget = new(
		id: "IJ0205",
		title: "Invalid [AssetCatalog] target",
		messageFormat: "[AssetCatalog] class '{0}' must be a top-level, non-generic, non-static partial class without a declared base class",
		category: "Assets",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor FileKindInvalid = new(
		id: "IJ0206",
		title: "Invalid [AssetFileKind]",
		messageFormat: "[AssetFileKind] suffix '{0}' is invalid: {1}",
		category: "Assets",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor BorrowedValueStored = new(
		id: "IJ0210",
		title: "Borrowed asset value stored beyond the borrow",
		messageFormat: "A value from AssetBorrow<T>.Value is {0}, which can outlive the borrow; borrow the asset again where it's used, or pin it (AssetBorrow<T>.Pin()) to keep that version",
		category: "Assets",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);
}
