// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.Internals.Analyzers.Attributes;

namespace Injure.Mods;

public sealed class OwnerOrderingException(string message) : Exception(message) {
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct OwnerOrderingConstraintKind {
	public enum Case {
		Soft = 1,
		Hard,
	}
}

public readonly struct OwnerOrderingConstraint {
	public string OwnerId { get; }
	public OwnerOrderingConstraintKind Kind { get; }

	private OwnerOrderingConstraint(string ownerId, OwnerOrderingConstraintKind kind) {
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		OwnerId = ownerId;
		Kind = kind;
	}

	public static OwnerOrderingConstraint Soft(string ownerId) => new(ownerId, OwnerOrderingConstraintKind.Soft);
	public static OwnerOrderingConstraint Hard(string ownerId) => new(ownerId, OwnerOrderingConstraintKind.Hard);
}

public sealed class OwnerOrderedEntry<T> {
	private readonly OwnerOrderingConstraint[] beforeOwners;
	private readonly OwnerOrderingConstraint[] afterOwners;

	public T Item { get; }
	public string OwnerId { get; }
	public string LocalId { get; }
	public int LocalPriority { get; }
	public IReadOnlyList<OwnerOrderingConstraint> BeforeOwners => beforeOwners;
	public IReadOnlyList<OwnerOrderingConstraint> AfterOwners => afterOwners;

	public OwnerOrderedEntry(
		T item,
		string ownerId,
		string localId,
		int localPriority = 0,
		IEnumerable<OwnerOrderingConstraint>? beforeOwners = null,
		IEnumerable<OwnerOrderingConstraint>? afterOwners = null
	) {
		ArgumentNullException.ThrowIfNull(item);
		ModMetadataValidation.ValidateOwnerIdOrThrow(ownerId);
		ModMetadataValidation.ValidateLocalIdOrThrow(localId);
		Item = item;
		OwnerId = ownerId;
		LocalId = localId;
		LocalPriority = localPriority;
		this.beforeOwners = fold(beforeOwners, nameof(beforeOwners));
		this.afterOwners = fold(afterOwners, nameof(afterOwners));
	}

	private static OwnerOrderingConstraint[] fold(IEnumerable<OwnerOrderingConstraint>? owners, string paramName) {
		if (owners is null)
			return Array.Empty<OwnerOrderingConstraint>();
		HashSet<string> seen = new(StringComparer.Ordinal);
		List<OwnerOrderingConstraint> list = new();
		foreach (OwnerOrderingConstraint cons in owners) {
			if (!seen.Add(cons.OwnerId))
				throw new ArgumentException("owner list cannot contain duplicates", paramName);
			list.Add(cons);
		}
		return list.Count == 0 ? Array.Empty<OwnerOrderingConstraint>() : list.ToArray();
	}
}

public static class OwnerOrderedSorter {
	private sealed class Node<T>(string ownerId) {
		public readonly string OwnerId = ownerId;
		public readonly HashSet<string> LocalIds = new(StringComparer.Ordinal);
		public readonly HashSet<string> Outgoing = new(StringComparer.Ordinal);
		public readonly List<OwnerOrderedEntry<T>> Items = new();
		public int InDegree;
	}

	private enum VisitState {
		Visiting,
		Done,
	}

	public static T[] Sort<T>(IReadOnlyList<OwnerOrderedEntry<T>> entries) {
		Dictionary<string, Node<T>> nodes = new(StringComparer.Ordinal);
		foreach (OwnerOrderedEntry<T> entry in entries) {
			if (!nodes.TryGetValue(entry.OwnerId, out Node<T>? node)) {
				node = new Node<T>(entry.OwnerId);
				nodes.Add(entry.OwnerId, node);
			}
			if (!node.LocalIds.Add(entry.LocalId))
				throw new OwnerOrderingException($"duplicate LocalId '{entry.LocalId}' for owner '{entry.OwnerId}'");
			node.Items.Add(entry);
		}

		foreach (Node<T> node in nodes.Values) {
			foreach (OwnerOrderedEntry<T> entry in node.Items) {
				foreach (OwnerOrderingConstraint before in entry.BeforeOwners)
					addConstraintEdge(nodes, entry, before, node.OwnerId, before.OwnerId, "before");
				foreach (OwnerOrderingConstraint after in entry.AfterOwners)
					addConstraintEdge(nodes, entry, after, after.OwnerId, node.OwnerId, "after");
			}
		}

		SortedSet<string> ready = new(StringComparer.Ordinal);
		foreach (Node<T> node in nodes.Values)
			if (node.InDegree == 0)
				ready.Add(node.OwnerId);

		List<Node<T>> ordered = new(nodes.Count);
		while (ready.Count > 0) {
			string ownerId = ready.Min!;
			ready.Remove(ownerId);
			Node<T> node = nodes[ownerId];
			ordered.Add(node);
			foreach (string nextID in node.Outgoing) {
				Node<T> next = nodes[nextID];
				if (--next.InDegree == 0)
					ready.Add(nextID);
			}
		}
		if (ordered.Count != nodes.Count) {
			List<string>? cycle = findCycle(nodes);
			string msg = "ordering constraints are unsatisfiable";
			if (cycle is not null)
				msg += ": " + string.Join(" -> ", cycle) + " -> " + cycle[0];
			throw new OwnerOrderingException(msg);
		}

		// sort within owners by local priority
		var result = new T[entries.Count];
		int resultidx = 0;
		foreach (Node<T> node in ordered) {
			node.Items.Sort(static (a, b) => {
					int n = a.LocalPriority.CompareTo(b.LocalPriority);
					if (n != 0)
						return n;
					n = StringComparer.Ordinal.Compare(a.LocalId, b.LocalId);
					if (n == 0)
						throw new InternalStateException("duplicate LocalId got into local-priority sort");
					return n;
				}
			);
			foreach (OwnerOrderedEntry<T> ent in node.Items)
				result[resultidx++] = ent.Item;
		}
		return result;
	}

	private static void addConstraintEdge<T>(
		Dictionary<string, Node<T>> nodes,
		OwnerOrderedEntry<T> entry,
		OwnerOrderingConstraint constraint,
		string fromOwnerId,
		string toOwnerId,
		string direction
	) {
		if (!nodes.ContainsKey(constraint.OwnerId)) {
			if (constraint.Kind == OwnerOrderingConstraintKind.Hard)
				throw new OwnerOrderingException(
					$"owner '{entry.OwnerId}' local '{entry.LocalId}' has hard '{direction}' constraint targeting unknown owner '{constraint.OwnerId}'"
				);
			return;
		}
		addEdge(nodes, fromOwnerId, toOwnerId);
	}

	private static void addEdge<T>(Dictionary<string, Node<T>> nodes, string fromOwnerId, string toOwnerId) {
		if (string.IsNullOrWhiteSpace(fromOwnerId) || string.IsNullOrWhiteSpace(toOwnerId))
			throw new OwnerOrderingException("null/empty/whitespace references are not allowed");
		if (StringComparer.Ordinal.Equals(fromOwnerId, toOwnerId))
			throw new OwnerOrderingException($"owner '{fromOwnerId}' has a self-reference");
		if (!nodes.TryGetValue(fromOwnerId, out Node<T>? fromNode))
			throw new OwnerOrderingException($"unknown source owner '{fromOwnerId}'");
		if (!nodes.TryGetValue(toOwnerId, out Node<T>? toNode))
			throw new OwnerOrderingException($"unknown target owner '{toOwnerId}'");
		if (fromNode.Outgoing.Add(toOwnerId))
			toNode.InDegree++;
	}

	private static List<string>? findCycle<T>(Dictionary<string, Node<T>> nodes) {
		HashSet<string> remaining = new(
			nodes.Where(static (kvp) => kvp.Value.InDegree > 0).Select(static (kvp) => kvp.Key),
			StringComparer.Ordinal
		);
		Dictionary<string, VisitState> state = new(StringComparer.Ordinal);
		// not a Stack<string> so we can find a node already on the stack easily
		List<string> stack = new();
		Dictionary<string, int> stackIndex = new(StringComparer.Ordinal);
		List<string>? cycle = null;

		// iterate by Ordinal order instead of the nondeterministic hashset iter order
		foreach (string id in remaining.OrderBy(static x => x, StringComparer.Ordinal)) {
			if (state.ContainsKey(id))
				continue;
			dfs(id);
			if (cycle is not null)
				return cycle;
		}
		return null;

		void dfs(string id) {
			state[id] = VisitState.Visiting;
			stackIndex[id] = stack.Count;
			stack.Add(id);
			foreach (string nextID in nodes[id].Outgoing.Where(remaining.Contains).OrderBy(static x => x, StringComparer.Ordinal))
				if (!state.TryGetValue(nextID, out VisitState nextState)) {
					dfs(nextID);
					if (cycle is not null)
						return;
				} else if (nextState == VisitState.Visiting) {
					int start = stackIndex[nextID];
					cycle = stack.GetRange(start, stack.Count - start);
					return;
				}
			stack.RemoveAt(stack.Count - 1);
			stackIndex.Remove(id);
			state[id] = VisitState.Done;
		}
	}
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
