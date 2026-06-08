// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class LifetimeObligation {
	private bool observedAnySatisfaction;
	private ObligationSatisfactionLevel bestObservedSatisfaction;
	private ObligationSatisfactionLevel worstObservedSatisfaction;
	private ObligationState state;
	private ObligationSatisfactionLevel stateSatisfaction;
	private Location? stateLocation;
	private readonly List<ObligationPathDivergence> pathDivergences = new();

	public LifetimeObligation(
		int id,
		LifetimeObligationKind kind,
		ObligationSatisfactionLevel requiredSatisfaction,
		ObligationSatisfactionLevel initialSatisfaction,
		ILocalSymbol? local,
		string typeName,
		Location originLocation
	) {
		ID = id;
		Kind = kind;
		RequiredSatisfaction = requiredSatisfaction;
		Local = local;
		TypeName = typeName;
		OriginLocation = originLocation;
		observedAnySatisfaction = initialSatisfaction != ObligationSatisfactionLevel.None;
		bestObservedSatisfaction = initialSatisfaction;
		worstObservedSatisfaction = initialSatisfaction;
		if (initialSatisfaction >= requiredSatisfaction) {
			state = ObligationState.Satisfied;
			stateSatisfaction = initialSatisfaction;
		} else {
			state = ObligationState.Uncertain;
			stateSatisfaction = ObligationSatisfactionLevel.None;
		}
		stateLocation = originLocation;
	}

	private LifetimeObligation(
		int id,
		LifetimeObligationKind kind,
		ObligationSatisfactionLevel requiredSatisfaction,
		ILocalSymbol? local,
		string typeName,
		Location originLocation,
		bool observedAnySatisfaction,
		ObligationSatisfactionLevel bestObservedSatisfaction,
		ObligationSatisfactionLevel worstObservedSatisfaction,
		ObligationState state,
		ObligationSatisfactionLevel stateSatisfaction,
		Location? stateLocation,
		List<PassedToCallFact> passedToCalls,
		List<ObligationPathDivergence> pathDivergences
	) {
		ID = id;
		Kind = kind;
		RequiredSatisfaction = requiredSatisfaction;
		Local = local;
		TypeName = typeName;
		OriginLocation = originLocation;
		this.observedAnySatisfaction = observedAnySatisfaction;
		this.bestObservedSatisfaction = bestObservedSatisfaction;
		this.worstObservedSatisfaction = worstObservedSatisfaction;
		this.state = state;
		this.stateSatisfaction = stateSatisfaction;
		this.stateLocation = stateLocation;
		PassedToCalls.AddRange(passedToCalls);
		this.pathDivergences.AddRange(pathDivergences);
	}

	public int ID { get; }
	public LifetimeObligationKind Kind { get; }
	public ObligationSatisfactionLevel RequiredSatisfaction { get; }
	public ILocalSymbol? Local { get; }
	public string TypeName { get; }
	public Location OriginLocation { get; }
	public ObligationState State => state;
	public bool ObservedAnySatisfaction => observedAnySatisfaction;
	public ObligationSatisfactionLevel BestObservedSatisfaction => bestObservedSatisfaction;
	public ObligationSatisfactionLevel WorstObservedSatisfaction => worstObservedSatisfaction;
	public Location? StateLocation => stateLocation;
	public List<PassedToCallFact> PassedToCalls { get; } = new();
	public ImmutableArray<ObligationPathDivergence> PathDivergences => pathDivergences.ToImmutableArray();

	public bool TrySatisfy(ObligationSatisfactionLevel level, Location location) {
		if (!observedAnySatisfaction) {
			observedAnySatisfaction = true;
			bestObservedSatisfaction = level;
			worstObservedSatisfaction = level;
		} else {
			if (level > bestObservedSatisfaction)
				bestObservedSatisfaction = level;
			if (level < worstObservedSatisfaction)
				worstObservedSatisfaction = level;
		}

		if (state != ObligationState.Uncertain)
			return false;
		if (level < RequiredSatisfaction)
			return false;
		state = ObligationState.Satisfied;
		stateSatisfaction = level;
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

	public void AddPathDivergence(ObligationPathDivergence divergence) {
		if (divergence.Left != divergence.Right)
			pathDivergences.Add(divergence);
	}

	private ObligationPathState satisfiedPathState() {
		ObligationPathState result = ObligationPathState.Satisfied;
		if (stateSatisfaction >= ObligationSatisfactionLevel.Method)
			result |= ObligationPathState.MethodSatisfied;
		if (stateSatisfaction >= ObligationSatisfactionLevel.Generation)
			result |= ObligationPathState.GenerationSatisfied;
		return result;
	}

	public ObligationPathState ToPathState() => state switch {
		ObligationState.Uncertain => ObligationPathState.Open,
		ObligationState.Satisfied => satisfiedPathState(),
		ObligationState.ExceptionLeaked => ObligationPathState.ExceptionLeaked,
		ObligationState.Leaked => ObligationPathState.Leaked,
		_ => throw new ArgumentOutOfRangeException(nameof(state)),
	};

	public bool HasPartialPathLeakEvidence() {
		foreach (ObligationPathDivergence d in pathDivergences)
			if (isSafePath(d.Left) && isOpenOrLeakedPath(d.Right) || isSafePath(d.Right) && isOpenOrLeakedPath(d.Left))
				return true;
		return false;
	}

	private static bool isSafePath(ObligationPathState state) =>
		(state & (ObligationPathState.Absent | ObligationPathState.Satisfied)) != 0;

	private static bool isOpenOrLeakedPath(ObligationPathState state) =>
		(state & (ObligationPathState.Open | ObligationPathState.Leaked | ObligationPathState.ExceptionLeaked)) != 0;

	public LifetimeObligation Clone() => new(
		ID,
		Kind,
		RequiredSatisfaction,
		Local,
		TypeName,
		OriginLocation,
		observedAnySatisfaction,
		bestObservedSatisfaction,
		worstObservedSatisfaction,
		state,
		stateSatisfaction,
		stateLocation,
		PassedToCalls,
		pathDivergences
	);

	private void mergeObservedSatisfactionFrom(LifetimeObligation other) {
		if (!other.observedAnySatisfaction) {
			worstObservedSatisfaction = ObligationSatisfactionLevel.None;
			return;
		}
		if (!observedAnySatisfaction) {
			observedAnySatisfaction = true;
			bestObservedSatisfaction = other.bestObservedSatisfaction;
			worstObservedSatisfaction = ObligationSatisfactionLevel.None;
			return;
		}
		if (other.bestObservedSatisfaction > bestObservedSatisfaction)
			bestObservedSatisfaction = other.bestObservedSatisfaction;
		if (other.worstObservedSatisfaction < worstObservedSatisfaction)
			worstObservedSatisfaction = other.worstObservedSatisfaction;
	}

	public static LifetimeObligation MergeWorst(LifetimeObligation left, LifetimeObligation right, Location location, FlowMergeKind kind) {
		LifetimeObligation result = left.Clone();
		foreach (ObligationPathDivergence divergence in right.pathDivergences)
			result.AddPathDivergence(divergence);

		ObligationPathState lState = left.ToPathState();
		ObligationPathState rState = right.ToPathState();
		if (lState != rState)
			result.AddPathDivergence(new ObligationPathDivergence(kind, location, lState, rState));

		if (stateRank(right.State) > stateRank(result.state)) {
			result.state = right.State;
			result.stateLocation = right.StateLocation ?? location;
		}
		result.mergeObservedSatisfactionFrom(right);
		result.PassedToCalls.AddRange(right.PassedToCalls);
		return result;
	}

	private static int stateRank(ObligationState state) => state switch {
		ObligationState.Satisfied => 0,
		ObligationState.Uncertain => 1,
		ObligationState.ExceptionLeaked => 2,
		ObligationState.Leaked => 3,
		_ => throw new ArgumentOutOfRangeException(nameof(state)),
	};
}
