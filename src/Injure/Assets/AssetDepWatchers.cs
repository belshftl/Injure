// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Io;

namespace Injure.Assets;

/// <summary>
/// Watches <see cref="FileAssetDep"/>s with a <see cref="FileHotReloadMonitor"/>, which reports a
/// file once it stopped changing.
/// </summary>
public sealed class FileAssetDepWatcher : IAssetDepWatcher<FileAssetDep> {
	private readonly FileHotReloadMonitor monitor;

	public event Action<FileAssetDep>? Changed;

	/// <summary>
	/// Creates a watcher.
	/// </summary>
	/// <param name="options">Monitor options, or <see langword="null"/> for the defaults.</param>
	public FileAssetDepWatcher(FileHotReloadMonitorOptions? options = null) {
		monitor = new FileHotReloadMonitor(options);
		monitor.StablePathChanged += onStablePathChanged;
	}

	public void Watch(FileAssetDep dep) => monitor.WatchFile(dep.FullPath);
	public void Unwatch(FileAssetDep dep) => monitor.UnwatchFile(dep.FullPath);
	public void Dispose() {
		monitor.StablePathChanged -= onStablePathChanged;
		monitor.Dispose();
	}
	private void onStablePathChanged(string fullPath) => Changed?.Invoke(new FileAssetDep(fullPath));
}
