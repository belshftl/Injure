// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Internals.Analyzers.Attributes;

namespace Injure.ModKit.Abstractions;

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

[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(ModAssemblyHotReloadLevel))]
public readonly partial struct ModReloadability {
	public enum Case {
		None = 1,
		SafeBoundary,
		Live,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModAssetManagementKind {
	public enum Case {
		None = 1,
		Tracked,
		Manual,
		Untracked,
	}
}

public readonly record struct GameCompatibilityManifest {
	public required Semver? TargetVersion { get; init; }
	public required Guid? TargetBuildMvid { get; init; }
}

public readonly record struct ModRelationshipManifest {
	public required string OwnerID { get; init; }
	public required ModRelationshipKind Kind { get; init; }
	public required Semver? Version { get; init; }
	public required string? Description { get; init; }
}

public readonly record struct ModAssetsManifest {
	public required ModAssetManagementKind ManagementKind { get; init; }
	public required string? Root { get; init; }
}

public readonly record struct ModNativeLibraryManifest {
	public required string ID { get; init; }
	public required string Path { get; init; }
	public required string RuntimeIdentifier { get; init; }
}

public abstract record ModManifest {
	public required string OwnerID { get; init; }
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
	public override ModReloadability Reloadability => Reloadable ? LiveReloadable ? ModReloadability.Live : ModReloadability.SafeBoundary : ModReloadability.None;
}

public sealed record ContentModManifest : ModManifest {
	public override ModReloadability Reloadability => Reloadable ? ModReloadability.Live : ModReloadability.None;
}
