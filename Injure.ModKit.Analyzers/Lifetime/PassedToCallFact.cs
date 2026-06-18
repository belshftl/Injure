// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal readonly record struct PassedToCallFact(
	IMethodSymbol TargetMethod,
	int ArgumentOrdinal,
	RefKind RefKind,
	Location Location
);
