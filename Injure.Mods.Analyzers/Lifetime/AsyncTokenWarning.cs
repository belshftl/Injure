// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Lifetime;

internal readonly record struct AsyncTokenWarning(
	IMethodSymbol TargetMethod,
	Location Location
);
