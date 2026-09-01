// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Detours;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Modif;

/// <remarks>
/// <para>
/// Changes are accumulated into a dirty set rather than immediately running work, so a mod load that
/// registers a thousand patches produces one re-transform pass and not a thousand.
/// </para>
/// <para>
/// Thread-safe, achieved by mutexing. Reads return immutable snapshots.
/// </para>
/// </remarks>
internal sealed class ModifRegistry {
	private sealed class MethodEntry {
		public UnsafeOwnerOrderedRegistry<IlManipulatorRegistration> Manipulators { get; } = new();
		public UnsafeOwnerOrderedRegistry<DetourRegistration> Detours { get; } = new();

		public MethodGeneration Generation { get; private set; } = MethodGeneration.None;

		public void BumpPatch() => Generation = Generation with { Patch = Generation.Patch + 1 };
		public void SetDetoured(bool detoured) => Generation = Generation with { HasDetourPrologue = detoured };
	}

	private readonly Lock @lock = new();
	private readonly Dictionary<MethodIdentity, MethodEntry> entries = new();
	private readonly HashSet<MethodIdentity> dirty = new();

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
			return entries.TryGetValue(method, out MethodEntry? entry)
				? entry.Manipulators.ReadSnapshot().ToImmutableArray()
				: [];
	}

	/// <summary>
	/// Gets a method's detours, in chain order.
	/// </summary>
	public ImmutableArray<DetourRegistration> GetDetours(MethodIdentity method) {
		lock (@lock)
			return entries.TryGetValue(method, out MethodEntry? entry)
				? entry.Detours.ReadSnapshot().ToImmutableArray()
				: [];
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
			manipulators = entry.Manipulators.ReadSnapshot().ToImmutableArray();
			detours = entry.Detours.ReadSnapshot().ToImmutableArray();
			return true;
		}
	}

	// ==========================================================================================
	// mutation
	public void AddManipulator(MethodIdentity method, OwnerOrderedEntry<IlManipulatorRegistration> entry) {
		InternalStateException.ThrowIfNull(entry);
		requireMatchingIdentifier(entry, entry.Item.OwnerId, entry.Item.LocalId);

		lock (@lock) {
			MethodEntry target = entryFor(method);
			requireUnusedIdentifier(target, entry.OwnerId, entry.LocalId);

			target.Manipulators.RegisterLocked(entry);
			target.BumpPatch();
			dirty.Add(method);
		}
	}

	public void AddDetour(MethodIdentity method, OwnerOrderedEntry<DetourRegistration> entry) {
		InternalStateException.ThrowIfNull(entry);
		requireMatchingIdentifier(entry, entry.Item.OwnerId, entry.Item.LocalId);

		lock (@lock) {
			MethodEntry target = entryFor(method);
			requireUnusedIdentifier(target, entry.OwnerId, entry.LocalId);

			bool wasDetoured = target.Detours.ReadSnapshot().Count > 0;
			target.Detours.RegisterLocked(entry);

			if (!wasDetoured) {
				target.SetDetoured(true);
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
			List<MethodIdentity> affected = [];
			foreach ((MethodIdentity method, MethodEntry entry) in entries.ToArray()) {
				bool wasDetoured = entry.Detours.ReadSnapshot().Count > 0;

				OwnerOrderedEntry<IlManipulatorRegistration>[] removedManipulators =
					entry.Manipulators.UnregisterAllByOwnerIdLocked(ownerId);
				OwnerOrderedEntry<DetourRegistration>[] removedDetours =
					entry.Detours.UnregisterAllByOwnerIdLocked(ownerId);

				if (removedManipulators.Length == 0 && removedDetours.Length == 0)
					continue;

				if (removedManipulators.Length > 0)
					entry.BumpPatch();
				if (wasDetoured && entry.Detours.ReadSnapshot().Count == 0)
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

			bool wasDetoured = entry.Detours.ReadSnapshot().Count > 0;
			bool removedManipulator = entry.Manipulators.UnregisterByOwnerAndLocalIdsLocked(ownerId, localId, out _);
			bool removedDetour = !removedManipulator
				&& entry.Detours.UnregisterByOwnerAndLocalIdsLocked(ownerId, localId, out _);

			if (!removedManipulator && !removedDetour)
				return false;

			if (removedManipulator)
				entry.BumpPatch();
			if (wasDetoured && entry.Detours.ReadSnapshot().Count == 0)
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

	private static void requireMatchingIdentifier<T>(OwnerOrderedEntry<T> entry, string ownerId, string localId) {
		if (entry.OwnerId != ownerId || entry.LocalId != localId)
			throw new InternalStateException($"OwnerOrderedEntry '{entry.OwnerId}::{entry.LocalId}' doesn't match the registration it wraps '{ownerId}::{localId}'");
	}

	private static void requireUnusedIdentifier(MethodEntry entry, string ownerId, string localId) {
		if (
			isUsed(entry.Manipulators.GetLocalIdsByOwnerLocked(), ownerId, localId) ||
			isUsed(entry.Detours.GetLocalIdsByOwnerLocked(), ownerId, localId)
		)
			throw new InternalStateException($"'{ownerId}::{localId}' is already registered against this method as a patch/detour");
	}

	private static bool isUsed(IReadOnlyDictionary<string, IReadOnlySet<string>> byOwner, string ownerId, string localId) =>
		byOwner.TryGetValue(ownerId, out IReadOnlySet<string>? used) && used.Contains(localId);

	private void pruneIfEmpty(MethodIdentity method, MethodEntry entry) {
		if (entry.Manipulators.ReadSnapshot().Count == 0 && entry.Detours.ReadSnapshot().Count == 0)
			entries.Remove(method);
	}
}
