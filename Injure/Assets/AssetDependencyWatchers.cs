// SPDX-License-Identifier: MIT

using System;

using Injure.IO;

namespace Injure.Assets;

/// <summary>
/// Asset dependency watcher for files built on top of <see cref="FileHotReloadMonitor"/>.
/// </summary>
public sealed class FileAssetDependencyWatcher : IAssetDependencyWatcher<FileAssetDependency> {
	private readonly FileHotReloadMonitor monitor;

	public event Action<FileAssetDependency>? Changed;

	public FileAssetDependencyWatcher(FileHotReloadMonitorOptions? options = null) {
		ArgumentNullException.ThrowIfNull(monitor);
		monitor = new FileHotReloadMonitor(options);
		monitor.StablePathChanged += onStablePathChanged;
	}

	public void Watch(FileAssetDependency dependency) => monitor.WatchFile(dependency.FullPath);
	public void Unwatch(FileAssetDependency dependency) => monitor.UnwatchFile(dependency.FullPath);
	public void Dispose() {
		monitor.StablePathChanged -= onStablePathChanged;
		monitor.Dispose();
	}
	private void onStablePathChanged(string fullPath) => Changed?.Invoke(new FileAssetDependency(fullPath));
}
