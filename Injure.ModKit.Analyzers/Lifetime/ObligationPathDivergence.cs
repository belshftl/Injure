// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal readonly record struct ObligationPathDivergence(
	FlowMergeKind Kind,
	Location Location,
	ObligationPathState Left,
	ObligationPathState Right
);
