// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Runtime;

internal readonly record struct UntypedLoadedDepInfo(
	string OwnerID,
	Semver Version,
	ReloadGeneration Generation,
	UntypedBoundedScopeImpl Scope
);

internal readonly record struct UntypedUntypedLoadedCodeDepInfo(
	string OwnerID,
	Semver Version,
	ReloadGeneration Generation,
	UntypedBoundedScopeImpl Scope,
	UntypedModExportTable Exports,
	Assembly Assembly,
	Type LifetimeIdentityType
);
