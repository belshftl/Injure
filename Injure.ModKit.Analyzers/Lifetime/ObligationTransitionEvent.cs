// SPDX-License-Identifier: MIT

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
	ObligationSatisfactionLevel BestObservedSatisfaction,
	Location Location
) {
	public static ObligationTransitionEvent From(LifetimeObligation obl, ObligationState state, ObligationTransitionCause cause, Location location) =>
		new(obl.ID, obl.Kind, state, cause, obl.Local?.Name ?? $"<unassigned {obl.DisplayName}>", obl.DisplayName, obl.RequiredSatisfaction, obl.BestObservedSatisfaction, location);
}
