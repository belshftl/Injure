// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Injure.ModKit.Analyzers.Lifetime;

internal sealed class FlowState {
	private readonly Dictionary<int, LifetimeObligation> byID = new();
	private readonly Dictionary<ILocalSymbol, int> byLocal = new(SymbolEqualityComparer.Default);

	public IEnumerable<LifetimeObligation> Obligations => byID.Values;

	public void Add(LifetimeObligation obl) {
		byID[obl.ID] = obl;
		if (obl.Local is not null)
			byLocal[obl.Local] = obl.ID;
	}

	public bool TryGetByLocal(ILocalSymbol local, out LifetimeObligation obl) {
		obl = null!;
		return byLocal.TryGetValue(local, out int id) && byID.TryGetValue(id, out obl!);
	}

	public void RemoveLocal(ILocalSymbol local) {
		if (!byLocal.TryGetValue(local, out int id))
			return;
		byLocal.Remove(local);
		byID.Remove(id);
	}

	public void TrySatisfyLocal(ILocalSymbol local, ObligationSatisfactionLevel level, Location location) {
		if (TryGetByLocal(local, out LifetimeObligation obl))
			obl.TrySatisfy(level, location);
	}

	public void TransitionOpenToLeaked(Location location, ObligationTransitionCause cause, List<ObligationTransitionEvent> events) {
		foreach (LifetimeObligation obl in byID.Values) {
			if (obl.State is not ObligationState.Uncertain and not ObligationState.ExceptionLeaked)
				continue;
			if (obl.TryLeak(location))
				events.Add(ObligationTransitionEvent.From(obl, ObligationState.Leaked, cause, location));
		}
	}

	public void TransitionOpenToExceptionLeaked(Location location, ObligationTransitionCause cause, List<ObligationTransitionEvent> events) {
		foreach (LifetimeObligation obl in byID.Values) {
			if (obl.State != ObligationState.Uncertain)
				continue;
			if (obl.TryExceptionLeak(location))
				events.Add(ObligationTransitionEvent.From(obl, ObligationState.ExceptionLeaked, cause, location));
		}
	}

	public FlowState Clone() {
		FlowState clone = new();
		foreach (LifetimeObligation obl in byID.Values)
			clone.Add(obl.Clone());
		return clone;
	}

	public ImmutableArray<LifetimeObligation> Snapshot() {
		ImmutableArray<LifetimeObligation>.Builder builder = ImmutableArray.CreateBuilder<LifetimeObligation>(byID.Count);
		foreach (LifetimeObligation obl in byID.Values)
			builder.Add(obl.Clone());
		return builder.ToImmutable();
	}

	public static FlowState MergeWorst(FlowState left, FlowState right, Location location, FlowMergeKind kind) {
		FlowState result = new();
		foreach (LifetimeObligation leftObligation in left.byID.Values) {
			if (right.byID.TryGetValue(leftObligation.ID, out LifetimeObligation? rightObligation)) {
				LifetimeObligation merged = LifetimeObligation.MergeWorst(leftObligation, rightObligation, location, kind);
				result.Add(merged);
			} else {
				LifetimeObligation merged = leftObligation.Clone();
				merged.AddPathDivergence(new ObligationPathDivergence(kind, location, leftObligation.ToPathState(), ObligationPathState.Absent));
				result.Add(merged);
			}
		}

		foreach (LifetimeObligation rightObligation in right.byID.Values) {
			if (left.byID.ContainsKey(rightObligation.ID))
				continue;
			LifetimeObligation merged = rightObligation.Clone();
			merged.AddPathDivergence(new ObligationPathDivergence(kind, location, ObligationPathState.Absent, rightObligation.ToPathState()));
			result.Add(merged);
		}

		return result;
	}
}
