// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Injure.IO;

namespace Injure.Mods.Runtime;

public readonly record struct ModFileChange(
	string OwnerID,
	bool Reloadable,
	string Path
);

public sealed class ModFileWatcher : IDisposable {
	private sealed class WatchedOwner(bool reloadable) {
		public bool Reloadable = reloadable;
		public readonly HashSet<string> Paths = new();
	}

	private readonly Lock @lock = new();
	private readonly FileHotReloadMonitor monitor;
	private readonly StringComparer pathComparer;
	private readonly Dictionary<string, HashSet<string>> ownersByPath;
	private readonly Dictionary<string, WatchedOwner> ownersByID;
	private bool disposed;

	public event Action<ModFileChange>? Changed;

	public ModFileWatcher(FileHotReloadMonitor? monitor = null) {
		this.monitor = monitor ?? new FileHotReloadMonitor();
		pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		ownersByPath = new Dictionary<string, HashSet<string>>(pathComparer);
		ownersByID = new Dictionary<string, WatchedOwner>(StringComparer.Ordinal);
		this.monitor.StablePathChanged += onStablePathChanged;
	}

	public void Watch(ModWatchSpec spec) {
		ArgumentException.ThrowIfNullOrWhiteSpace(spec.OwnerID);
		ArgumentException.ThrowIfNullOrWhiteSpace(spec.ManifestPath);

		string manifestPath = Path.GetFullPath(spec.ManifestPath);
		string? entryAssemblyPath = spec.EntryAssemblyPath is null ? null : Path.GetFullPath(spec.EntryAssemblyPath);
		List<string> pathsToWatch = new();

		lock (@lock) {
			ObjectDisposedException.ThrowIf(disposed, this);

			if (!ownersByID.TryGetValue(spec.OwnerID, out WatchedOwner? owner)) {
				owner = new WatchedOwner(spec.Reloadable);
				ownersByID.Add(spec.OwnerID, owner);
			} else {
				owner.Reloadable = spec.Reloadable;
			}
			addPathLocked(spec.OwnerID, owner, manifestPath, pathsToWatch);
			if (entryAssemblyPath is not null)
				addPathLocked(spec.OwnerID, owner, entryAssemblyPath, pathsToWatch);
		}

		foreach (string path in pathsToWatch)
			monitor.WatchFile(path);
	}

	public void Unwatch(string ownerID) {
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerID);
		List<string> pathsToUnwatch = new();

		lock (@lock) {
			ObjectDisposedException.ThrowIf(disposed, this);

			if (!ownersByID.Remove(ownerID, out WatchedOwner? owner))
				return;
			foreach (string path in owner.Paths) {
				if (!ownersByPath.TryGetValue(path, out HashSet<string>? owners))
					continue;
				owners.Remove(ownerID);
				if (owners.Count == 0) {
					ownersByPath.Remove(path);
					pathsToUnwatch.Add(path);
				}
			}
		}

		foreach (string path in pathsToUnwatch)
			monitor.UnwatchFile(path);
	}

	public void RebuildFrom(IEnumerable<ModWatchSpec> specs) {
		ArgumentNullException.ThrowIfNull(specs);

		ModWatchSpec[] snapshot = specs.ToArray();
		string[] oldOwners;
		lock (@lock) {
			ObjectDisposedException.ThrowIf(disposed, this);
			oldOwners = ownersByID.Keys.ToArray();
		}

		foreach (string ownerID in oldOwners)
			Unwatch(ownerID);
		foreach (ModWatchSpec spec in snapshot)
			Watch(spec);
	}

	private void addPathLocked(string ownerID, WatchedOwner owner, string fullPath, List<string> pathsToWatch) {
		if (owner.Paths.Add(fullPath)) {
			if (!ownersByPath.TryGetValue(fullPath, out HashSet<string>? owners)) {
				owners = new HashSet<string>(StringComparer.Ordinal);
				ownersByPath.Add(fullPath, owners);
				pathsToWatch.Add(fullPath);
			}
			owners.Add(ownerID);
		}
	}

	private void onStablePathChanged(string path) {
		string fullPath;
		try {
			fullPath = Path.GetFullPath(path);
		} catch {
			return;
		}

		ModFileChange[] changes;
		lock (@lock) {
			if (disposed)
				return;

			if (!ownersByPath.TryGetValue(fullPath, out HashSet<string>? ownerIDs))
				return;
			List<ModFileChange> result = new(ownerIDs.Count);
			foreach (string ownerID in ownerIDs) {
				if (!ownersByID.TryGetValue(ownerID, out WatchedOwner? owner))
					continue;
				result.Add(new ModFileChange(OwnerID: ownerID, Reloadable: owner.Reloadable, Path: fullPath));
			}
			changes = result.ToArray();
		}

		foreach (ModFileChange change in changes)
			Changed?.Invoke(change);
	}

	public void Dispose() {
		List<string> toUnwatch;

		lock (@lock) {
			if (disposed)
				return;
			disposed = true;

			toUnwatch = ownersByPath.Keys.ToList();
			ownersByPath.Clear();
			ownersByID.Clear();
		}

		monitor.StablePathChanged -= onStablePathChanged;
		foreach (string path in toUnwatch)
			try { monitor.UnwatchFile(path); } catch {}
		monitor.Dispose();
	}
}
