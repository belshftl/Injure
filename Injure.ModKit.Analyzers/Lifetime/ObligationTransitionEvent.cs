// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal readonly record struct ObligationTransitionEvent(
	int ObligationID,
	LifetimeObligationKind Kind,
	ObligationState NewState,
	ObligationTransitionCause Cause,
	string SubjectName,
	string DisplayName,
	ObligationSatisfactionLevel RequiredSatisfaction,
	bool ObservedAnySatisfaction,
	ObligationSatisfactionLevel BestObservedSatisfaction,
	ObligationSatisfactionLevel WorstObservedSatisfaction,
	ObligationLeakKind LeakKind,
	ImmutableArray<ObligationPathDivergence> PathDivergences,
	Location Location
) {
	public static ObligationTransitionEvent From(LifetimeObligation obl, ObligationState state, ObligationTransitionCause cause, Location location) => new(
		obl.ID,
		obl.Kind,
		state,
		cause,
		obl.Local?.Name ?? $"<unassigned {obl.DisplayName}>",
		obl.DisplayName,
		obl.RequiredSatisfaction,
		obl.ObservedAnySatisfaction,
		obl.BestObservedSatisfaction,
		obl.WorstObservedSatisfaction,
		obl.HasPartialPathLeakEvidence() ? ObligationLeakKind.OnlySomePaths : ObligationLeakKind.AllKnownPaths,
		obl.PathDivergences,
		location
	);
}
