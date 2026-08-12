// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Mods;

/// <summary>
/// Thrown when owner-ordering entries or constraints cannot form a valid deterministic order.
/// </summary>
/// <param name="message">
/// Message describing the invalid entry, missing hard target, self-reference, or constraint cycle.
/// </param>
public sealed class OwnerOrderingException(string message) : Exception(message) {
}

/// <summary>
/// Specifies how an ordering constraint behaves when its target is absent.
/// </summary>
/// <remarks>
/// <para>
/// A soft constraint is ignored when its target is absent. A hard constraint requires
/// its target to be present. Soft constraints are otherwise enforced identically to hard constraints;
/// self-references, cycles, or other unsatisfisable conditions still cause ordering to fail.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct OwnerOrderingConstraintKind {
	/// <summary>Raw switch tag for <see cref="OwnerOrderingConstraintKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// The constraint is to be ignored if its target is absent.
		/// </summary>
		Soft = 1,

		/// <summary>
		/// Ordering is to fail if this constraint's target is absent.
		/// </summary>
		Hard,
	}
}

/// <summary>
/// Specifies whether an ordering constraint targets an entire owner or one exact entry.
/// </summary>
/// <remarks>
/// An owner target orders the source entry's entire owner relative to the target owner.
/// An entry target orders only the source entry relative to the exact target entry.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct OwnerOrderingConstraintTargetKind {
	public enum Case {
		Owner = 1,
		Entry,
	}
}

/// <summary>
/// Identifies the target of an owner-ordering constraint, that being either an entire owner or
/// one exact entry identified by an owner ID + local ID pair.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly struct OwnerOrderingConstraintTarget : IEquatable<OwnerOrderingConstraintTarget> {
	/// <summary>
	/// Whether this value targets an entire owner or one exact entry.
	/// </summary>
	public OwnerOrderingConstraintTargetKind Kind { get; }

	/// <summary>
	/// The target owner ID.
	/// </summary>
	public string OwnerId { get; }

	/// <summary>
	/// If <see cref="Kind"/> is <see cref="OwnerOrderingConstraintTargetKind.Entry"/>, the target local ID;
	/// otherwise, <see langword="null"/>.
	/// </summary>
	public string? LocalId { get; }

	private OwnerOrderingConstraintTarget(OwnerOrderingConstraintTargetKind kind, string ownerId, string? localId) {
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		if (kind == OwnerOrderingConstraintTargetKind.Owner) {
			if (localId is not null)
				throw new ArgumentException("an owner target cannot have a local ID", nameof(localId));
		} else if (kind == OwnerOrderingConstraintTargetKind.Entry) {
			ModMetadataValidation.ValidateLocalIdOrThrow(localId);
		} else {
			throw new UnreachableException();
		}

		Kind = kind;
		OwnerId = ownerId;
		LocalId = localId;
	}

	public static OwnerOrderingConstraintTarget Owner(string ownerId) =>
		new(OwnerOrderingConstraintTargetKind.Owner, ownerId, null);
	public static OwnerOrderingConstraintTarget Entry(string ownerId, string localId) =>
		new(OwnerOrderingConstraintTargetKind.Entry, ownerId, localId);

	public bool Equals(OwnerOrderingConstraintTarget other) => Kind == other.Kind && OwnerId == other.OwnerId && LocalId == other.LocalId;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is OwnerOrderingConstraintTarget other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Kind, OwnerId, LocalId);
	public static bool operator ==(OwnerOrderingConstraintTarget left, OwnerOrderingConstraintTarget right) => left.Equals(right);
	public static bool operator !=(OwnerOrderingConstraintTarget left, OwnerOrderingConstraintTarget right) => !left.Equals(right);

	public override string ToString() => Kind == OwnerOrderingConstraintTargetKind.Entry ? $"{OwnerId}::{LocalId}" : OwnerId;
}

public readonly struct OwnerOrderingConstraint {
	public OwnerOrderingConstraintTarget Target { get; }
	public OwnerOrderingConstraintKind Kind { get; }

	private OwnerOrderingConstraint(OwnerOrderingConstraintTarget target, OwnerOrderingConstraintKind kind) {
		validateTarget(target);
		Target = target;
		Kind = kind;
	}

	public static OwnerOrderingConstraint SoftOwner(string ownerId) =>
		Soft(OwnerOrderingConstraintTarget.Owner(ownerId));

	public static OwnerOrderingConstraint HardOwner(string ownerId) =>
		Hard(OwnerOrderingConstraintTarget.Owner(ownerId));

	public static OwnerOrderingConstraint SoftEntry(string ownerId, string localId) =>
		Soft(OwnerOrderingConstraintTarget.Entry(ownerId, localId));

	public static OwnerOrderingConstraint HardEntry(string ownerId, string localId) =>
		Hard(OwnerOrderingConstraintTarget.Entry(ownerId, localId));

	public static OwnerOrderingConstraint Soft(OwnerOrderingConstraintTarget target) =>
		new(target, OwnerOrderingConstraintKind.Soft);

	public static OwnerOrderingConstraint Hard(OwnerOrderingConstraintTarget target) =>
		new(target, OwnerOrderingConstraintKind.Hard);

	private static void validateTarget(OwnerOrderingConstraintTarget target) {
		ModMetadataValidation.ValidateOwnerIdOrThrow(target.OwnerId);
		if (target.Kind == OwnerOrderingConstraintTargetKind.Entry)
			ModMetadataValidation.ValidateLocalIdOrThrow(target.LocalId);
		else if (target.LocalId is not null)
			throw new ArgumentException("an owner target cannot have a local ID", nameof(target));
	}
}

public sealed class OwnerOrderedEntry<T> {
	private readonly OwnerOrderingConstraint[] before;
	private readonly OwnerOrderingConstraint[] after;

	public T Item { get; }
	public string OwnerId { get; }
	public string LocalId { get; }
	public int LocalPriority { get; }

	public IReadOnlyList<OwnerOrderingConstraint> Before => before;
	public IReadOnlyList<OwnerOrderingConstraint> After => after;

	public OwnerOrderedEntry(
		T item,
		string ownerId,
		string localId,
		int localPriority = 0,
		IEnumerable<OwnerOrderingConstraint>? before = null,
		IEnumerable<OwnerOrderingConstraint>? after = null
	) {
		ArgumentNullException.ThrowIfNull(item);
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		ModMetadataValidation.ValidateLocalIdOrThrow(localId);
		Item = item;
		OwnerId = ownerId;
		LocalId = localId;
		LocalPriority = localPriority;
		this.before = fold(before, nameof(before));
		this.after = fold(after, nameof(after));
	}

	private static OwnerOrderingConstraint[] fold(IEnumerable<OwnerOrderingConstraint>? constraints, string paramName) {
		if (constraints is null)
			return Array.Empty<OwnerOrderingConstraint>();
		HashSet<OwnerOrderingConstraintTarget> seen = new();
		List<OwnerOrderingConstraint> list = new();
		foreach (OwnerOrderingConstraint constraint in constraints) {
			if (!seen.Add(constraint.Target))
				throw new ArgumentException($"constraint list contains duplicate target '{constraint.Target}'", paramName);
			list.Add(constraint);
		}
		return list.Count == 0 ? Array.Empty<OwnerOrderingConstraint>() : list.ToArray();
	}
}

public static class OwnerOrderedSorter {
	private sealed class OwnerNode<T>(string ownerId) {
		public readonly string OwnerId = ownerId;
		public readonly HashSet<string> LocalIds = new(StringComparer.Ordinal);
		public readonly HashSet<string> OutgoingOwners = new(StringComparer.Ordinal);
		public readonly List<EntryNode<T>> Entries = new();
		public int InDegree;
	}

	private sealed class EntryNode<T>(OwnerOrderedEntry<T> entry, OwnerNode<T> owner) {
		public readonly OwnerOrderedEntry<T> Entry = entry;
		public readonly OwnerNode<T> Owner = owner;
		public readonly HashSet<EntryNode<T>> Outgoing = new();
		public int InDegree;
		public int BaselineIndex;
	}

	private enum VisitState {
		Visiting,
		Done,
	}

	public static T[] Sort<T>(IReadOnlyList<OwnerOrderedEntry<T>> entries) {
		ArgumentNullException.ThrowIfNull(entries);
		Dictionary<string, OwnerNode<T>> owners = new(StringComparer.Ordinal);
		Dictionary<(string OwnerId, string LocalId), EntryNode<T>> entriesById = new();

		foreach (OwnerOrderedEntry<T> entry in entries) {
			ArgumentNullException.ThrowIfNull(entry);

			if (!owners.TryGetValue(entry.OwnerId, out OwnerNode<T>? owner)) {
				owner = new OwnerNode<T>(entry.OwnerId);
				owners.Add(entry.OwnerId, owner);
			}
			if (!owner.LocalIds.Add(entry.LocalId))
				throw new OwnerOrderingException($"duplicate LocalId '{entry.LocalId}' for owner '{entry.OwnerId}'");

			EntryNode<T> node = new(entry, owner);
			owner.Entries.Add(node);
			entriesById.Add((entry.OwnerId, entry.LocalId), node);
		}

		foreach (OwnerNode<T> owner in owners.Values)
			owner.Entries.Sort(static (a, b) => {
					int cmp = a.Entry.LocalPriority.CompareTo(b.Entry.LocalPriority);
					if (cmp != 0)
						return cmp;
					cmp = StringComparer.Ordinal.Compare(a.Entry.LocalId, b.Entry.LocalId);
					if (cmp == 0)
						throw new InternalStateException("duplicate LocalId got into local-priority sort");
					return cmp;
				}
			);

		bool hasEntryConstraints = false;

		foreach (OwnerNode<T> owner in owners.Values) {
			foreach (EntryNode<T> source in owner.Entries) {
				foreach (OwnerOrderingConstraint constraint in source.Entry.Before)
					if (constraint.Target.Kind == OwnerOrderingConstraintTargetKind.Owner)
						addOwnerConstraintEdge(owners, source.Entry, constraint, source.Entry.OwnerId, constraint.Target.OwnerId, "before");
					else
						hasEntryConstraints = true;

				foreach (OwnerOrderingConstraint constraint in source.Entry.After)
					if (constraint.Target.Kind == OwnerOrderingConstraintTargetKind.Owner)
						addOwnerConstraintEdge(owners, source.Entry, constraint, constraint.Target.OwnerId, source.Entry.OwnerId, "after");
					else
						hasEntryConstraints = true;
			}
		}

		List<OwnerNode<T>> orderedOwners = sortOwners(owners);
		return !hasEntryConstraints
			? flattenOwners(orderedOwners, entries.Count)
			: sortEntries(owners, orderedOwners, entriesById, entries.Count);
	}

	private static List<OwnerNode<T>> sortOwners<T>(Dictionary<string, OwnerNode<T>> owners) {
		SortedSet<string> ready = new(StringComparer.Ordinal);

		foreach (OwnerNode<T> owner in owners.Values)
			if (owner.InDegree == 0)
				ready.Add(owner.OwnerId);
		List<OwnerNode<T>> ordered = new(owners.Count);

		while (ready.Count > 0) {
			string ownerId = ready.Min!;
			ready.Remove(ownerId);
			OwnerNode<T> owner = owners[ownerId];
			ordered.Add(owner);
			foreach (string nextId in owner.OutgoingOwners) {
				OwnerNode<T> next = owners[nextId];
				if (--next.InDegree == 0)
					ready.Add(nextId);
			}
		}

		if (ordered.Count != owners.Count) {
			List<string>? cycle = findOwnerCycle(owners);
			string msg = "ordering constraints are unsatisfiable";
			if (cycle is not null)
				msg += ": " + string.Join(" -> ", cycle) + " -> " + cycle[0];
			throw new OwnerOrderingException(msg);
		}

		return ordered;
	}

	private static T[] flattenOwners<T>(IReadOnlyList<OwnerNode<T>> orderedOwners, int count) {
		var result = new T[count];
		int i = 0;
		foreach (OwnerNode<T> owner in orderedOwners)
			foreach (EntryNode<T> entry in owner.Entries)
				result[i++] = entry.Entry.Item;
		return result;
	}

	private static T[] sortEntries<T>(
		Dictionary<string, OwnerNode<T>> owners,
		IReadOnlyList<OwnerNode<T>> orderedOwners,
		Dictionary<(string OwnerId, string LocalId), EntryNode<T>> entriesById,
		int count
	) {
		var entriesByBaseline = new EntryNode<T>[count];
		int baselineIndex = 0;
		foreach (OwnerNode<T> owner in orderedOwners) {
			foreach (EntryNode<T> entry in owner.Entries) {
				entry.BaselineIndex = baselineIndex;
				entriesByBaseline[baselineIndex++] = entry;
			}
		}

		foreach (OwnerNode<T> owner in owners.Values)
			for (int i = 1; i < owner.Entries.Count; i++)
				addEntryEdge(owner.Entries[i - 1], owner.Entries[i]);

		// owner-level A -> B means every entry of A precedes every entry of B,
		// and since entries within each owner are chained, this requires only
		// one edge (from A's last entry to B's first)
		foreach (OwnerNode<T> owner in owners.Values) {
			EntryNode<T> lastSource = owner.Entries[^1];
			foreach (string targetOwnerId in owner.OutgoingOwners)
				addEntryEdge(lastSource, owners[targetOwnerId].Entries[0]);
		}

		foreach (OwnerNode<T> owner in owners.Values) {
			foreach (EntryNode<T> source in owner.Entries) {
				foreach (OwnerOrderingConstraint constraint in source.Entry.Before) {
					if (constraint.Target.Kind != OwnerOrderingConstraintTargetKind.Entry)
						continue;
					addEntryConstraintEdge(entriesById, source, constraint, source, "before");
				}

				foreach (OwnerOrderingConstraint constraint in source.Entry.After) {
					if (constraint.Target.Kind != OwnerOrderingConstraintTargetKind.Entry)
						continue;
					addEntryConstraintEdge(entriesById, source, constraint, source, "after");
				}
			}
		}

		SortedSet<int> ready = new();

		foreach (EntryNode<T> entry in entriesByBaseline)
			if (entry.InDegree == 0)
				ready.Add(entry.BaselineIndex);
		var result = new T[count];
		int resultIndex = 0;

		while (ready.Count > 0) {
			int idx = ready.Min;
			ready.Remove(idx);
			EntryNode<T> entry = entriesByBaseline[idx];
			result[resultIndex++] = entry.Entry.Item;
			foreach (EntryNode<T> next in entry.Outgoing)
				if (--next.InDegree == 0)
					ready.Add(next.BaselineIndex);
		}

		if (resultIndex != count) {
			List<EntryNode<T>>? cycle = findEntryCycle(entriesByBaseline);
			string msg = "ordering constraints are unsatisfiable";
			if (cycle is not null)
				msg += ": " + string.Join(" -> ", cycle.Select(formatEntryId)) + " -> " + formatEntryId(cycle[0]);
			throw new OwnerOrderingException(msg);
		}

		return result;
	}

	private static void addOwnerConstraintEdge<T>(
		Dictionary<string, OwnerNode<T>> owners,
		OwnerOrderedEntry<T> sourceEntry,
		OwnerOrderingConstraint constraint,
		string fromOwnerId,
		string toOwnerId,
		string direction
	) {
		if (!owners.ContainsKey(constraint.Target.OwnerId)) {
			if (constraint.Kind == OwnerOrderingConstraintKind.Hard)
				throw new OwnerOrderingException(
					$"owner '{sourceEntry.OwnerId}' local '{sourceEntry.LocalId}' has hard '{direction}' constraint targeting unknown owner '{constraint.Target.OwnerId}'"
				);
			return;
		}
		addOwnerEdge(owners, fromOwnerId, toOwnerId);
	}

	private static void addOwnerEdge<T>(
		Dictionary<string, OwnerNode<T>> owners,
		string fromOwnerId,
		string toOwnerId
	) {
		if (string.IsNullOrWhiteSpace(fromOwnerId) || string.IsNullOrWhiteSpace(toOwnerId))
			throw new OwnerOrderingException("null/empty/whitespace references are not allowed");
		if (fromOwnerId == toOwnerId)
			throw new OwnerOrderingException($"owner '{fromOwnerId}' has an owner-level self-reference");
		if (!owners.TryGetValue(fromOwnerId, out OwnerNode<T>? from))
			throw new OwnerOrderingException($"unknown source owner '{fromOwnerId}'");
		if (!owners.TryGetValue(toOwnerId, out OwnerNode<T>? to))
			throw new OwnerOrderingException($"unknown target owner '{toOwnerId}'");
		if (from.OutgoingOwners.Add(toOwnerId))
			to.InDegree++;
	}

	private static void addEntryConstraintEdge<T>(
		Dictionary<(string OwnerId, string LocalId), EntryNode<T>> entriesById,
		EntryNode<T> source,
		OwnerOrderingConstraint constraint,
		EntryNode<T> declaringEntry,
		string direction
	) {
		string targetLocalId = constraint.Target.LocalId ?? throw new InternalStateException("entry constraint has no local ID");
		if (!entriesById.TryGetValue((constraint.Target.OwnerId, targetLocalId), out EntryNode<T>? target)) {
			if (constraint.Kind == OwnerOrderingConstraintKind.Hard)
				throw new OwnerOrderingException(
					$"owner '{declaringEntry.Entry.OwnerId}' local '{declaringEntry.Entry.LocalId}' has hard '{direction}' constraint targeting unknown entry '{constraint.Target.OwnerId}::{targetLocalId}'"
				);
			return;
		}
		if (direction == "before")
			addEntryEdge(source, target);
		else if (direction == "after")
			addEntryEdge(target, source);
		else
			throw new InternalStateException($"unknown ordering direction '{direction}'");
	}

	private static void addEntryEdge<T>(EntryNode<T> from, EntryNode<T> to) {
		if (ReferenceEquals(from, to))
			throw new OwnerOrderingException($"entry '{formatEntryId(from)}' has a self-reference");
		if (from.Outgoing.Add(to))
			to.InDegree++;
	}

	private static List<string>? findOwnerCycle<T>(Dictionary<string, OwnerNode<T>> owners) {
		HashSet<string> remaining = new(owners.Where(static kvp => kvp.Value.InDegree > 0).Select(static kvp => kvp.Key), StringComparer.Ordinal);
		Dictionary<string, VisitState> state = new(StringComparer.Ordinal);
		List<string> stack = new();
		Dictionary<string, int> stackIndex = new(StringComparer.Ordinal);
		List<string>? cycle = null;
		foreach (string ownerId in remaining.OrderBy(static id => id, StringComparer.Ordinal)) {
			if (state.ContainsKey(ownerId))
				continue;
			visit(ownerId);
			if (cycle is not null)
				return cycle;
		}
		return null;

		void visit(string ownerId) {
			state[ownerId] = VisitState.Visiting;
			stackIndex[ownerId] = stack.Count;
			stack.Add(ownerId);
			foreach (string nextId in owners[ownerId].OutgoingOwners.Where(remaining.Contains).OrderBy(static id => id, StringComparer.Ordinal))
				if (!state.TryGetValue(nextId, out VisitState nextState)) {
					visit(nextId);
					if (cycle is not null)
						return;
				} else if (nextState == VisitState.Visiting) {
					int start = stackIndex[nextId];
					cycle = stack.GetRange(start, stack.Count - start);
					return;
				}
			stack.RemoveAt(stack.Count - 1);
			stackIndex.Remove(ownerId);
			state[ownerId] = VisitState.Done;
		}
	}

	private static List<EntryNode<T>>? findEntryCycle<T>(IReadOnlyList<EntryNode<T>> entries) {
		HashSet<EntryNode<T>> remaining = new(entries.Where(static e => e.InDegree > 0));
		Dictionary<EntryNode<T>, VisitState> state = new();
		List<EntryNode<T>> stack = new();
		Dictionary<EntryNode<T>, int> stackIndex = new();
		List<EntryNode<T>>? cycle = null;
		foreach (EntryNode<T> entry in remaining.OrderBy(static e => e.BaselineIndex)) {
			if (state.ContainsKey(entry))
				continue;
			visit(entry);
			if (cycle is not null)
				return cycle;
		}
		return null;

		void visit(EntryNode<T> entry) {
			state[entry] = VisitState.Visiting;
			stackIndex[entry] = stack.Count;
			stack.Add(entry);
			foreach (EntryNode<T> next in entry.Outgoing.Where(remaining.Contains).OrderBy(static next => next.BaselineIndex))
				if (!state.TryGetValue(next, out VisitState nextState)) {
					visit(next);
					if (cycle is not null)
						return;
				} else if (nextState == VisitState.Visiting) {
					int start = stackIndex[next];
					cycle = stack.GetRange(start, stack.Count - start);
					return;
				}
			stack.RemoveAt(stack.Count - 1);
			stackIndex.Remove(entry);
			state[entry] = VisitState.Done;
		}
	}

	private static string formatEntryId<T>(EntryNode<T> entry) => $"{entry.Entry.OwnerId}::{entry.Entry.LocalId}";
}

/// <summary>
/// Maintains owner-ordered entries and publishes read-only snapshots of their sorted values.
/// </summary>
/// <remarks>
/// <para>
/// This is single-writer, multiple-reader. Writes (or any other methods that end in `<c>Locked</c>`)
/// must be externally mutexed/synchronized, otherwise they will race and corrupt state.
/// </para>
/// <para>
/// <see cref="ReadSnapshot"/> may be called concurrently, including concurrently with writes.
/// It returns the last successfully published snapshot. Mutating operations publish a new
/// snapshot only if sorting succeeds.
/// </para>
/// </remarks>
public sealed class UnsafeOwnerOrderedRegistry<T> {
	private readonly record struct AddedEntry(ulong ID, string OwnerId, string LocalId, bool CreatedOwnerSet);
	private readonly record struct RemovedEntry(ulong ID, OwnerOrderedEntry<T> Entry, bool RemovedLocalId, bool RemovedOwnerSet);

	private readonly Dictionary<ulong, OwnerOrderedEntry<T>> entries = new();
	private readonly Dictionary<string, HashSet<string>> localIdsByOwner = new(StringComparer.Ordinal);
	private readonly Dictionary<(string OwnerId, string LocalId), ulong> idsByOwnerLocalId = new();
	private ulong nextID = 0; // first ID will be 1 since this gets incremented upfront
	private T[] snapshot = Array.Empty<T>();

	public ulong RegisterLocked(OwnerOrderedEntry<T> entry) {
		ArgumentNullException.ThrowIfNull(entry);
		ulong oldNextID = nextID;
		List<AddedEntry> added = new(capacity: 1);
		try {
			ulong id = addEntry(entry, added);

			T[] s = OwnerOrderedSorter.Sort(entries.Values.ToArray());
			Volatile.Write(ref snapshot, s);
			return id;
		} catch {
			rollbackAdded(added);
			nextID = oldNextID;
			throw;
		}
	}

	public ulong[] RegisterManyLocked(IReadOnlyList<OwnerOrderedEntry<T>> entries) {
		ArgumentNullException.ThrowIfNull(entries);
		if (entries.Count == 0)
			return Array.Empty<ulong>();

		ulong oldNextID = nextID;
		List<AddedEntry> added = new(capacity: entries.Count);
		ulong[] ids = new ulong[entries.Count];
		try {
			for (int i = 0; i < entries.Count; i++) {
				OwnerOrderedEntry<T> entry = entries[i];
				ArgumentNullException.ThrowIfNull(entry);
				ids[i] = addEntry(entry, added);
			}

			T[] s = OwnerOrderedSorter.Sort(this.entries.Values.ToArray());
			Volatile.Write(ref snapshot, s);
			return ids;
		} catch {
			rollbackAdded(added);
			nextID = oldNextID;
			throw;
		}
	}

	public bool UnregisterLocked(ulong id, [NotNullWhen(true)] out OwnerOrderedEntry<T>? removed) {
		List<RemovedEntry> removedEntries = new(capacity: 1);
		try {
			if (!removeEntry(id, removedEntries)) {
				removed = null;
				return false;
			}

			T[] s = OwnerOrderedSorter.Sort(entries.Values.ToArray());
			Volatile.Write(ref snapshot, s);
			removed = removedEntries[0].Entry;
			return true;
		} catch {
			rollbackRemoved(removedEntries);
			throw;
		}
	}

	public OwnerOrderedEntry<T>[] UnregisterManyLocked(IReadOnlySet<ulong> ids) {
		ArgumentNullException.ThrowIfNull(ids);
		if (ids.Count == 0)
			return Array.Empty<OwnerOrderedEntry<T>>();

		List<RemovedEntry> removedEntries = new(capacity: ids.Count);
		try {
			foreach (ulong id in ids)
				removeEntry(id, removedEntries);
			if (removedEntries.Count == 0)
				return Array.Empty<OwnerOrderedEntry<T>>();

			T[] s = OwnerOrderedSorter.Sort(entries.Values.ToArray());
			Volatile.Write(ref snapshot, s);
			var result = new OwnerOrderedEntry<T>[removedEntries.Count];
			for (int i = 0; i < removedEntries.Count; i++)
				result[i] = removedEntries[i].Entry;
			return result;
		} catch {
			rollbackRemoved(removedEntries);
			throw;
		}
	}

	/// <summary>
	/// Removes entries en masse by owner ID instead of by registration handle.
	/// </summary>
	/// <remarks>
	/// Primarily intended for situations like cleanup when an owner ID is gone and has leftover registrations.
	/// Prefer <see cref="UnregisterLocked(ulong, out OwnerOrderedEntry{T}?)"/> for most cases.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID.
	/// </exception>
	public OwnerOrderedEntry<T>[] UnregisterAllByOwnerIdLocked(string ownerId) {
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		if (!localIdsByOwner.TryGetValue(ownerId, out HashSet<string>? localIds))
			return Array.Empty<OwnerOrderedEntry<T>>();

		List<RemovedEntry> removedEntries = new(capacity: localIds.Count);
		try {
			ulong[] ids = new ulong[localIds.Count];
			int i = 0;
			foreach (string localId in localIds)
				ids[i++] = idsByOwnerLocalId[(ownerId, localId)];

			for (i = 0; i < ids.Length; i++)
				removeEntry(ids[i], removedEntries);

			T[] s = OwnerOrderedSorter.Sort(entries.Values.ToArray());
			Volatile.Write(ref snapshot, s);

			var result = new OwnerOrderedEntry<T>[removedEntries.Count];
			for (i = 0; i < removedEntries.Count; i++)
				result[i] = removedEntries[i].Entry;
			return result;
		} catch {
			rollbackRemoved(removedEntries);
			throw;
		}
	}

	/// <summary>
	/// Removes an entry by owner/local ID instead of by registration handle.
	/// </summary>
	/// <remarks>
	/// Primarily intended for situations like cleanup when the registration handle was lost.
	/// Prefer <see cref="UnregisterLocked(ulong, out OwnerOrderedEntry{T}?)"/> for most cases.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ownerId"/> is not a valid owner ID or if <paramref name="localId"/>
	/// is not a valid local ID.
	/// </exception>
	public bool UnregisterByOwnerAndLocalIdsLocked(string ownerId, string localId, [NotNullWhen(true)] out OwnerOrderedEntry<T>? removed) {
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		ModMetadataValidation.ValidateLocalIdOrThrow(localId);
		ArgumentNullException.ThrowIfNull(localId);
		if (!idsByOwnerLocalId.TryGetValue((ownerId, localId), out ulong id)) {
			removed = null;
			return false;
		}
		return UnregisterLocked(id, out removed);
	}

	public ulong[] ReplaceManyLocked(IReadOnlySet<ulong> remove, IReadOnlyList<OwnerOrderedEntry<T>> add) {
		ArgumentNullException.ThrowIfNull(remove);
		ArgumentNullException.ThrowIfNull(add);
		if (remove.Count == 0 && add.Count == 0)
			return Array.Empty<ulong>();

		ulong oldNextID = nextID;
		List<RemovedEntry> removed = new(remove.Count);
		List<AddedEntry> added = new(add.Count);
		ulong[] ids = new ulong[add.Count];
		try {
			foreach (ulong id in remove)
				removeEntry(id, removed);
			for (int i = 0; i < add.Count; i++) {
				OwnerOrderedEntry<T> entry = add[i];
				ArgumentNullException.ThrowIfNull(entry);
				ids[i] = addEntry(entry, added);
			}

			if (removed.Count != 0 || added.Count != 0) {
				T[] s = OwnerOrderedSorter.Sort(entries.Values.ToArray());
				Volatile.Write(ref snapshot, s);
			}
			return ids;
		} catch {
			rollbackAdded(added);
			rollbackRemoved(removed);
			nextID = oldNextID;
			throw;
		}
	}

	public IReadOnlyList<T> ReadSnapshot() => Volatile.Read(ref snapshot);

	private ulong addEntry(OwnerOrderedEntry<T> entry, List<AddedEntry> added) {
		bool createdLocalIdSet = false;
		if (!localIdsByOwner.TryGetValue(entry.OwnerId, out HashSet<string>? localIds)) {
			localIds = new HashSet<string>(StringComparer.Ordinal);
			localIdsByOwner.Add(entry.OwnerId, localIds);
			createdLocalIdSet = true;
		}
		if (!localIds.Add(entry.LocalId))
			throw new OwnerOrderingException($"duplicate LocalId '{entry.LocalId}' for owner '{entry.OwnerId}'");
		ulong id = checked(nextID + 1);
		entries.Add(id, entry);
		idsByOwnerLocalId.Add((entry.OwnerId, entry.LocalId), id);
		nextID = id;
		added.Add(new AddedEntry(id, entry.OwnerId, entry.LocalId, createdLocalIdSet));
		return id;
	}

	private bool removeEntry(ulong id, List<RemovedEntry> removedEntries) {
		if (!entries.TryGetValue(id, out OwnerOrderedEntry<T>? removed))
			return false;
		HashSet<string> localIds = localIdsByOwner[removed.OwnerId];
		entries.Remove(id);
		idsByOwnerLocalId.Remove((removed.OwnerId, removed.LocalId));
		bool removedLocalId = localIds.Remove(removed.LocalId);
		bool removedOwnerSet = false;
		if (localIds.Count == 0) {
			localIdsByOwner.Remove(removed.OwnerId);
			removedOwnerSet = true;
		}
		removedEntries.Add(new RemovedEntry(id, removed, removedLocalId, removedOwnerSet));
		return true;
	}

	private void rollbackAdded(List<AddedEntry> added) {
		for (int i = added.Count - 1; i >= 0; i--) {
			AddedEntry entry = added[i];
			entries.Remove(entry.ID);
			idsByOwnerLocalId.Remove((entry.OwnerId, entry.LocalId));
			HashSet<string> localIds = localIdsByOwner[entry.OwnerId];
			localIds.Remove(entry.LocalId);
			if (entry.CreatedOwnerSet)
				localIdsByOwner.Remove(entry.OwnerId);
		}
	}

	private void rollbackRemoved(List<RemovedEntry> removedEntries) {
		for (int i = removedEntries.Count - 1; i >= 0; i--) {
			RemovedEntry removed = removedEntries[i];
			entries.Add(removed.ID, removed.Entry);
			idsByOwnerLocalId.Add((removed.Entry.OwnerId, removed.Entry.LocalId), removed.ID);
			HashSet<string> localIds;
			if (removed.RemovedOwnerSet) {
				localIds = new HashSet<string>(StringComparer.Ordinal);
				localIdsByOwner.Add(removed.Entry.OwnerId, localIds);
			} else {
				localIds = localIdsByOwner[removed.Entry.OwnerId];
			}
			if (removed.RemovedLocalId)
				localIds.Add(removed.Entry.LocalId);
		}
	}
}

/// <summary>
/// Maintains owner-ordered entries and publishes read-only snapshots of their sorted values.
/// </summary>
/// <remarks>
/// Thread-safe; concurrent reads are lock-free, concurrent writes are internally mutexed.
/// If you wish to do external synchronization of writes, see <see cref="UnsafeOwnerOrderedRegistry{T}"/>.
/// </remarks>
public sealed class OwnerOrderedRegistry<T> {
	private readonly Lock @lock = new();
	private readonly UnsafeOwnerOrderedRegistry<T> inner = new();

	public ulong Register(OwnerOrderedEntry<T> entry) {
		lock (@lock)
			return inner.RegisterLocked(entry);
	}

	public ulong[] RegisterMany(IReadOnlyList<OwnerOrderedEntry<T>> entries) {
		lock (@lock)
			return inner.RegisterManyLocked(entries);
	}

	public bool Unregister(ulong id, [NotNullWhen(true)] out OwnerOrderedEntry<T>? removed) {
		lock (@lock)
			return inner.UnregisterLocked(id, out removed);
	}

	public OwnerOrderedEntry<T>[] UnregisterMany(IReadOnlySet<ulong> ids) {
		lock (@lock)
			return inner.UnregisterManyLocked(ids);
	}

	public OwnerOrderedEntry<T>[] UnregisterAllByOwnerId(string ownerId) {
		lock (@lock)
			return inner.UnregisterAllByOwnerIdLocked(ownerId);
	}

	public bool UnregisterByOwnerAndLocalIds(string ownerId, string localId, [NotNullWhen(true)] out OwnerOrderedEntry<T>? removed) {
		lock (@lock)
			return inner.UnregisterByOwnerAndLocalIdsLocked(ownerId, localId, out removed);
	}

	public ulong[] ReplaceMany(IReadOnlySet<ulong> remove, IReadOnlyList<OwnerOrderedEntry<T>> add) {
		lock (@lock)
			return inner.ReplaceManyLocked(remove, add);
	}

	public IReadOnlyList<T> ReadSnapshot() => inner.ReadSnapshot();
}
