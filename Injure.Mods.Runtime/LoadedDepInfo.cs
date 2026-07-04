// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace Injure.Mods.Runtime;

internal readonly record struct UntypedLoadedDepInfo(
	string OwnerId,
	Semver Version,
	ReloadGeneration Generation,
	UntypedBoundedScopeImpl Scope
);

internal readonly record struct UntypedUntypedLoadedCodeDepInfo(
	string OwnerId,
	Semver Version,
	ReloadGeneration Generation,
	UntypedBoundedScopeImpl Scope,
	UntypedModExportTable Exports,
	Assembly Assembly,
	Type LifetimeIdentityType
);
