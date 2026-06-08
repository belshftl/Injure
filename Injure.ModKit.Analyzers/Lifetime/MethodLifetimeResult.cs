// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class MethodLifetimeResult {
	public required bool AnalysisBailedOut { get; init; }
	public required Location? BailoutLocation { get; init; }
	public required string? BailoutReason { get; init; }
	public required ImmutableArray<LifetimeObligation> Obligations { get; init; }
	public required ImmutableArray<ObligationTransitionEvent> Events { get; init; }
	public required ImmutableArray<AsyncTokenWarning> AsyncTokenWarnings { get; init; }
}
