// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Detours;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Modif;

/// <remarks>
/// <para>
/// Ordering is by owner first, then by intra-owner registration order. The owner order comes from
/// the caller because resolving it is the job of whatever higher up handles load order.
/// </para>
/// <para>
/// Changes are accumulated into a dirty set rather than immediately running work, so a mod load that
/// registers a thousand patches produces one re-transform pass and not a thousand.
/// </para>
/// <para>
/// Thread-safe, achieved by mutexing. Reads return immutable snapshots.
/// </para>
/// </remarks>
internal sealed class ModifRegistry {
	private readonly record struct Ordered<T>(T Registration, ulong Sequence);

	private sealed class MethodEntry {
		public List<Ordered<IlManipulatorRegistration>> Manipulators { get; } = new();
		public List<Ordered<DetourRegistration>> Detours { get; } = new();
		public Dictionary<string, HashSet<string>> TakenIdPairs { get; } = new();
		public MethodGeneration Generation { get; private set; } = MethodGeneration.None;

		public bool TryOccupyIdPair(string ownerId, string localId) {
			if (TakenIdPairs.TryGetValue(ownerId, out HashSet<string>? localIds)) {
				return localIds.Add(localId);
			} else {
				localIds = new HashSet<string>(StringComparer.Ordinal) { localId };
				TakenIdPairs.Add(ownerId, localIds);
				return true;
			}
		}

		public void RemoveIdPair(string ownerId, string localId) {
			if (!TakenIdPairs.TryGetValue(ownerId, out HashSet<string>? localIds) || !localIds.Remove(localId))
				throw new InternalStateException($"nothing is registered under '{ownerId}::{localId}'");
			if (localIds.Count == 0)
				_ = TakenIdPairs.Remove(ownerId);
		}

		public void RemoveIdsByOwner(string ownerId) {
			if (!TakenIdPairs.Remove(ownerId))
				throw new InternalStateException($"nothing is registered under owner ID '{ownerId}'");
		}

		public void BumpPatch() => Generation = Generation with { Patch = Generation.Patch + 1 };

		public void SetDetoured(bool detoured) => Generation = Generation with { HasDetourPrologue = detoured };

		public ImmutableArray<IlManipulatorRegistration> OrderedManipulators(IComparer<string> ownerOrder) =>
			order(Manipulators, ownerOrder, static r => r.OwnerId);

		public ImmutableArray<DetourRegistration> OrderedDetours(IComparer<string> ownerOrder) =>
			order(Detours, ownerOrder, static r => r.OwnerId);

		private static ImmutableArray<T> order<T>(
			List<Ordered<T>> registrs,
			IComparer<string> ownerOrder,
			Func<T, string> ownerOf
		) {
			Ordered<T>[] sorted = registrs.ToArray();
			Array.Sort(sorted, (a, b) => {
				int byOwner = ownerOrder.Compare(ownerOf(a.Registration), ownerOf(b.Registration));
				return byOwner != 0 ? byOwner : a.Sequence.CompareTo(b.Sequence);
			});
			return sorted.Select(static o => o.Registration).ToImmutableArray();
		}
	}

	private readonly IComparer<string> ownerOrder;
	private readonly Lock @lock = new();
	private readonly Dictionary<MethodIdentity, MethodEntry> entries = new();
	private readonly HashSet<MethodIdentity> dirty = new();
	private ulong nextSeq;

	/// <param name="ownerOrder">
	/// Orders owners against each other. Is an <see cref="IComparer{T}"/> to avoid the registry from
	/// having to reinvent load order resolution or be aware of it.
	/// </param>
	public ModifRegistry(IComparer<string> ownerOrder) {
		InternalStateException.ThrowIfNull(ownerOrder);
		this.ownerOrder = ownerOrder;
	}

	public ImmutableArray<MethodIdentity> ModifiedMethods {
		get {
			lock (@lock)
				return entries.Keys.ToImmutableArray();
		}
	}

	/// <summary>
	/// Gets the current generation of a method, or <see cref="MethodGeneration.None"/> if nothing is
	/// registered against it.
	/// </summary>
	public MethodGeneration GetGeneration(MethodIdentity method) {
		lock (@lock)
			return entries.TryGetValue(method, out MethodEntry? entry) ? entry.Generation : MethodGeneration.None;
	}

	/// <summary>
	/// Gets a method's manipulators, in application order.
	/// </summary>
	public ImmutableArray<IlManipulatorRegistration> GetManipulators(MethodIdentity method) {
		lock (@lock)
			return entries.TryGetValue(method, out MethodEntry? entry) ? entry.OrderedManipulators(ownerOrder) : [];
	}

	/// <summary>
	/// Gets a method's detours, in chain order.
	/// </summary>
	public ImmutableArray<DetourRegistration> GetDetours(MethodIdentity method) {
		lock (@lock)
			return entries.TryGetValue(method, out MethodEntry? entry) ? entry.OrderedDetours(ownerOrder) : [];
	}

	/// <summary>
	/// Attempts to get information about a method, or returns <see langword="false"/> if no entry exists.
	/// </summary>
	/// <param name="method">The method in question.</param>
	/// <param name="generation">Current generation of the method.</param>
	/// <param name="manipulators">The method's manipulators in application order.</param>
	/// <param name="detours">The method's detours in application order.</param>
	public bool TryGetSnapshot(
		MethodIdentity method,
		out MethodGeneration generation,
		out ImmutableArray<IlManipulatorRegistration> manipulators,
		out ImmutableArray<DetourRegistration> detours
	) {
		lock (@lock) {
			if (!entries.TryGetValue(method, out MethodEntry? entry)) {
				generation = MethodGeneration.None;
				manipulators = [];
				detours = [];
				return false;
			}
			generation = entry.Generation;
			manipulators = entry.OrderedManipulators(ownerOrder);
			detours = entry.OrderedDetours(ownerOrder);
			return true;
		}
	}


	// ==========================================================================================
	// mutation
	public void AddManipulator(MethodIdentity method, IlManipulatorRegistration reg) {
		InternalStateException.ThrowIfNull(reg);
		lock (@lock) {
			MethodEntry entry = entryFor(method);
			if (!entry.TryOccupyIdPair(reg.OwnerId, reg.LocalId))
				throw new InternalStateException($"'{reg.OwnerId}::{reg.LocalId}' is already registered against {method}");
			entry.Manipulators.Add(new Ordered<IlManipulatorRegistration>(reg, checked(nextSeq++)));
			entry.BumpPatch();
			dirty.Add(method);
		}
	}

	public void AddDetour(MethodIdentity method, DetourRegistration reg) {
		InternalStateException.ThrowIfNull(reg);
		lock (@lock) {
			MethodEntry entry = entryFor(method);
			if (!entry.TryOccupyIdPair(reg.OwnerId, reg.LocalId))
				throw new InternalStateException($"'{reg.OwnerId}::{reg.LocalId}' is already registered against {method}");
			bool wasDetoured = entry.Detours.Count > 0;
			entry.Detours.Add(new Ordered<DetourRegistration>(reg, checked(nextSeq++)));
			// only the transition into being detoured changes the emitted body
			if (!wasDetoured) {
				entry.SetDetoured(true);
				dirty.Add(method);
			}
		}
	}

	/// <remarks>
	/// The affected methods are marked dirty rather than reverted. Removing one owner's registrations
	/// while others remain is a re-transform from the baseline, not a revert, so the caller re-runs
	/// them exactly as it would after an addition.
	/// </remarks>
	/// <returns>
	/// The methods the owner had registrations against.
	/// </returns>
	public ImmutableArray<MethodIdentity> RemoveOwner(string ownerId) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		lock (@lock) {
			List<MethodIdentity> affected = new();
			foreach ((MethodIdentity method, MethodEntry entry) in entries.ToArray()) {
				bool wasDetoured = entry.Detours.Count > 0;
				int manipulators = entry.Manipulators.RemoveAll(m => m.Registration.OwnerId == ownerId);
				int detours = entry.Detours.RemoveAll(m => m.Registration.OwnerId == ownerId);
				if (manipulators == 0 && detours == 0)
					continue;

				entry.RemoveIdsByOwner(ownerId);

				if (manipulators > 0)
					entry.BumpPatch();
				if (wasDetoured && entry.Detours.Count == 0)
					entry.SetDetoured(false);
				dirty.Add(method);
				affected.Add(method);
				pruneIfEmpty(method, entry);
			}
			return affected.ToImmutableArray();
		}
	}

	/// <returns>
	/// Whether anything was removed.
	/// </returns>
	public bool Remove(MethodIdentity method, string ownerId, string localId) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		InternalStateException.ThrowIfInvalidLocalId(localId);
		lock (@lock) {
			if (!entries.TryGetValue(method, out MethodEntry? entry))
				return false;

			int manipulators = entry.Manipulators.RemoveAll(m => m.Registration.OwnerId == ownerId && m.Registration.LocalId == localId);
			bool wasDetoured = entry.Detours.Count > 0;
			int detours = entry.Detours.RemoveAll(d => d.Registration.OwnerId == ownerId && d.Registration.LocalId == localId);
			if (manipulators == 0 && detours == 0)
				return false;

			if (manipulators > 1)
				throw new InternalStateException($"method had more than 1 manipulator registered under '{ownerId}::{localId}'");
			if (detours > 1)
				throw new InternalStateException($"method had more than 1 detour registered under '{ownerId}::{localId}'");
			if (manipulators == 1 && detours == 1)
				throw new InternalStateException($"method had both a manipulator and a detour registered under '{ownerId}::{localId}'");
			entry.RemoveIdPair(ownerId, localId);

			if (manipulators == 1)
				entry.BumpPatch();
			if (wasDetoured && entry.Detours.Count == 0)
				entry.SetDetoured(false);
			dirty.Add(method);
			pruneIfEmpty(method, entry);
			return true;
		}
	}

	/// <summary>
	/// Drops every registration against methods in a module.
	/// </summary>
	/// <remarks>
	/// For module unload, where the methods themselves are going away. Nothing is marked dirty,
	/// because there is nothing left to re-transform.
	/// </remarks>
	public void RemoveModule(ModuleId module) {
		lock (@lock) {
			foreach (MethodIdentity method in entries.Keys.Where(m => m.Module == module).ToArray()) {
				entries.Remove(method);
				dirty.Remove(method);
			}
		}
	}

	/// <summary>
	/// Drains and returns the set of methods whose registrations changed since the last call.
	/// </summary>
	/// <remarks>
	/// Draining is destructive so that a caller can't process the same change twice. A method that
	/// changes again while a transform is running reappears in the next drain, which is correct, as said
	/// transform was from a generation that is now old.
	/// </remarks>
	public ImmutableArray<MethodIdentity> DrainDirty() {
		lock (@lock) {
			if (dirty.Count == 0)
				return [];
			var drained = dirty.ToImmutableArray();
			dirty.Clear();
			return drained;
		}
	}

	/// <summary>
	/// Marks methods as needing another pass.
	/// </summary>
	/// <remarks>
	/// For cases where a set was drained and whatever work was needed to be done with it could
	/// not be finished; re-marking is idempotent and never clobbers changes that arrived in the meantime.
	/// </remarks>
	public void MarkDirty(ImmutableArray<MethodIdentity> methods) {
		if (methods.IsDefaultOrEmpty)
			return;
		lock (@lock)
			foreach (MethodIdentity method in methods)
				if (entries.ContainsKey(method))
					dirty.Add(method);
	}

	// ==========================================================================================
	// helpers
	private MethodEntry entryFor(MethodIdentity method) {
		if (!entries.TryGetValue(method, out MethodEntry? entry)) {
			entry = new MethodEntry();
			entries[method] = entry;
		}
		return entry;
	}

	private void pruneIfEmpty(MethodIdentity method, MethodEntry entry) {
		if (entry.Manipulators.Count == 0 && entry.Detours.Count == 0)
			entries.Remove(method);
	}
}
