// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Mods.Analyzers.Lifetime;

internal readonly record struct ObligationPathDivergence(
	FlowMergeKind Kind,
	Location Location,
	ObligationPathState Left,
	ObligationPathState Right
);
