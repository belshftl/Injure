// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Mods.Abstractions;

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModRelationshipKind {
	public enum Case {
		RequiresSelfAfter = 1,
		RequiresSelfBefore,
		IfPresentSelfAfter,
		IfPresentSelfBefore,
		Conflicts,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(ModAssemblyHotReloadLevel))]
public readonly partial struct ModReloadability {
	public enum Case {
		None = 1,
		SafeBoundary,
		Live,
	}
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModAssetManagementKind {
	public enum Case {
		None = 1,
		Tracked,
		Manual,
		Untracked,
	}
}

/// <remarks>
/// The <see langword="default"/> value is valid and targets no particular game version or build.
/// </remarks>
public readonly record struct GameCompatibilityManifest {
	public required Semver? TargetVersion { get; init; }
	public required Guid? TargetBuildMvid { get; init; }
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct ModRelationshipManifest : IHasInvalidDefault {
	// explicit so that object initializers don't trip IJ0100; `required` makes them set every member
	public ModRelationshipManifest() {}

	public required string OwnerId { get; init; }
	public required ModRelationshipKind Kind { get; init; }
	public required Semver? Version { get; init; }
	public required string? Description { get; init; }

	bool IHasInvalidDefault.IsDefault => Kind == default;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct ModAssetsManifest : IHasInvalidDefault {
	// explicit so that object initializers don't trip IJ0100; `required` makes them set every member
	public ModAssetsManifest() {}

	public required ModAssetManagementKind ManagementKind { get; init; }
	public required string? Root { get; init; }

	bool IHasInvalidDefault.IsDefault => ManagementKind == default;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct ModNativeLibraryManifest : IHasInvalidDefault {
	// explicit so that object initializers don't trip IJ0100; `required` makes them set every member
	public ModNativeLibraryManifest() {}

	public required string Id { get; init; }
	public required string Path { get; init; }
	public required string RuntimeIdentifier { get; init; }

	bool IHasInvalidDefault.IsDefault => Id is null;
}

public abstract record ModManifest {
	public required string OwnerId { get; init; }
	public required Semver Version { get; init; }
	public required bool Reloadable { get; init; }

	public required string? DisplayName { get; init; }
	public required string? Description { get; init; }
	public required string? LicenseSpdx { get; init; }

	public required GameCompatibilityManifest Game { get; init; }
	public required IReadOnlyList<ModRelationshipManifest> Relationships { get; init; }
	public required ModAssetsManifest Assets { get; init; }
	public required IReadOnlyList<ModNativeLibraryManifest> NativeLibraries { get; init; }

	public abstract ModReloadability Reloadability { get; }
}

public sealed record CodeModManifest : ModManifest {
	public required string EntryAssembly { get; init; }
	public required bool LiveReloadable { get; init; }
	public required IReadOnlyList<string> ContractAssemblies { get; init; }

	public override ModReloadability Reloadability => Reloadable
		? LiveReloadable
			? ModReloadability.Live
			: ModReloadability.SafeBoundary
		: ModReloadability.None;
}

public sealed record ContentModManifest : ModManifest {
	public override ModReloadability Reloadability => Reloadable ? ModReloadability.Live : ModReloadability.None;
}
