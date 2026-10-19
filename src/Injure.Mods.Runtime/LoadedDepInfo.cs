// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions;

namespace Injure.Mods.Runtime;

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct UntypedLoadedDepInfo(
	string OwnerId,
	Semver Version,
	ReloadGeneration Generation,
	UntypedBoundedScopeImpl Scope
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => OwnerId is null;
}

/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct UntypedUntypedLoadedCodeDepInfo(
	string OwnerId,
	Semver Version,
	ReloadGeneration Generation,
	UntypedBoundedScopeImpl Scope,
	UntypedModExportTable Exports,
	Assembly Assembly,
	Type LifetimeIdentityType
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => OwnerId is null;
}
