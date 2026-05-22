// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class LifetimeObligation {
	private ObligationState state;
	private ObligationSatisfactionLevel bestObservedSatisfaction;
	private Location? stateLocation;

	public LifetimeObligation(
		int id,
		LifetimeObligationKind kind,
		ObligationSatisfactionLevel requiredSatisfaction,
		ObligationSatisfactionLevel initialSatisfaction,
		ILocalSymbol? local,
		string displayName,
		Location originLocation
	) {
		ID = id;
		Kind = kind;
		RequiredSatisfaction = requiredSatisfaction;
		Local = local;
		DisplayName = displayName;
		OriginLocation = originLocation;
		state = initialSatisfaction == ObligationSatisfactionLevel.None ? ObligationState.Uncertain : ObligationState.Satisfied;
		bestObservedSatisfaction = initialSatisfaction;
		stateLocation = originLocation;
	}

	private LifetimeObligation(
		int id,
		LifetimeObligationKind kind,
		ObligationSatisfactionLevel requiredSatisfaction,
		ILocalSymbol? local,
		string displayName,
		Location originLocation,
		ObligationState state,
		ObligationSatisfactionLevel bestObservedSatisfaction,
		Location? stateLocation,
		List<PassedToCallFact> passedToCalls
	) {
		ID = id;
		Kind = kind;
		RequiredSatisfaction = requiredSatisfaction;
		Local = local;
		DisplayName = displayName;
		OriginLocation = originLocation;
		this.state = state;
		this.bestObservedSatisfaction = bestObservedSatisfaction;
		this.stateLocation = stateLocation;
		PassedToCalls.AddRange(passedToCalls);
	}

	public int ID { get; }
	public LifetimeObligationKind Kind { get; }
	public ObligationSatisfactionLevel RequiredSatisfaction { get; }
	public ILocalSymbol? Local { get; }
	public string DisplayName { get; }
	public Location OriginLocation { get; }
	public ObligationState State => state;
	public ObligationSatisfactionLevel BestObservedSatisfaction => bestObservedSatisfaction;
	public Location? StateLocation => stateLocation;
	public List<PassedToCallFact> PassedToCalls { get; } = new List<PassedToCallFact>();

	public bool TrySatisfy(ObligationSatisfactionLevel level, Location location) {
		if (level > bestObservedSatisfaction)
			bestObservedSatisfaction = level;
		if (state != ObligationState.Uncertain)
			return false;
		if (!ObligationSatisfactionLevels.Satisfies(level, RequiredSatisfaction))
			return false;
		state = ObligationState.Satisfied;
		stateLocation = location;
		return true;
	}

	public bool TryExceptionLeak(Location location) {
		if (state != ObligationState.Uncertain)
			return false;
		state = ObligationState.ExceptionLeaked;
		stateLocation = location;
		return true;
	}

	public bool TryLeak(Location location) {
		if (state == ObligationState.Leaked || state == ObligationState.Satisfied)
			return false;
		state = ObligationState.Leaked;
		stateLocation = location;
		return true;
	}

	public LifetimeObligation Clone() =>
		new(ID, Kind, RequiredSatisfaction, Local, DisplayName, OriginLocation, state, bestObservedSatisfaction, stateLocation, PassedToCalls);

	public static LifetimeObligation MergeWorst(LifetimeObligation left, LifetimeObligation right, Location location) {
		LifetimeObligation result = left.Clone();
		if (stateRank(right.State) > stateRank(result.state)) {
			result.state = right.State;
			result.stateLocation = right.StateLocation ?? location;
		}
		if (right.BestObservedSatisfaction > result.bestObservedSatisfaction)
			result.bestObservedSatisfaction = right.BestObservedSatisfaction;
		result.PassedToCalls.AddRange(right.PassedToCalls);
		return result;
	}

	private static int stateRank(ObligationState state) => state switch {
		ObligationState.Satisfied => 0,
		ObligationState.Uncertain => 1,
		ObligationState.ExceptionLeaked => 2,
		ObligationState.Leaked => 3,
		_ => 3,
	};
}
