// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.CodeAnalysis.Internal;

namespace Injure.Assets;

/// <summary>
/// Describes an external dependency of a published asset version.
/// </summary>
/// <remarks>
/// <para>
/// Dependencies are collected while an asset version is being prepared and are published together
/// with that version. Dependency watchers may use them to automatically queue reloads when the
/// underlying external resource changes.
/// </para>
/// <para>
/// Implementations must be immutable value-like objects (e.g records) with meaningful equality,
/// since dependency tracking and watcher subscription are based on value identity rather than
/// object identity.
/// </para>
/// </remarks>
public interface IAssetDep {
	/// <summary>
	/// A human-readable description of this asset dependency, usable for diagnostics, failure reports,
	/// and debugging.
	/// </summary>
	/// <remarks>
	/// Must be stable for the lifetime of the dependency value; if that's violated, it probably won't
	/// work as expected either way since this string may be cached. Not opt-out; returning
	/// <see langword="null"/> or, worse, throwing is bad behavior.
	/// </remarks>
	string DebugDescription { get; }
}

/// <summary>
/// Dependency representing a file on the local filesystem.
/// </summary>
/// <param name="FullPath">Full path to the file.</param>
public sealed record FileAssetDep(string FullPath) : IAssetDep {
	public string DebugDescription => FullPath;
}

/// <summary>
/// Dependency representing an embedded assembly resource.
/// </summary>
/// <param name="Assembly">Assembly containing the resource.</param>
/// <param name="ResourcePath">Manifest resource name.</param>
public sealed record EmbeddedAssetDep(Assembly Assembly, string ResourcePath) : IAssetDep {
	public string DebugDescription => $"{Assembly}:{ResourcePath}";
}

/// <summary>
/// Records dependencies discovered while preparing an asset version.
/// </summary>
[DontImplement]
public interface IAssetDepCollector {
	/// <summary>
	/// Adds a dependency to the current asset preparation.
	/// </summary>
	/// <remarks>
	/// Dependencies are deduplicated by value equality.
	/// </remarks>
	void Add(IAssetDep dep);
}

/// <summary>
/// Watches external dependencies of a specific type and reports when they change.
/// </summary>
/// <typeparam name="T">
/// Dependency type handled by this watcher. Exact dependency-type matching is used; the watcher
/// observes dependencies of type <typeparamref name="T"/> but not its derived types.
/// </typeparam>
/// <remarks>
/// <para>
/// Dependency watchers are registered within an <see cref="AssetStore"/> and are used to
/// automatically queue reloads for published asset versions that depend on matching external
/// resources.
/// </para>
/// <para>
/// <see cref="Watch(T)"/> and <see cref="Unwatch(T)"/> are expected to be quick, non-reentrant, and
/// non-throwing in normal operation. <b>They must not call back into the owning
/// <see cref="AssetStore"/> or raise <see cref="Changed"/>.</b>
/// </para>
/// <para>
/// <see cref="Changed"/> may be raised from any thread and may be duplicate, coalesced, or delayed.
/// </para>
/// </remarks>
public interface IAssetDepWatcher<T> : IDisposable where T : IAssetDep {
	/// <summary>
	/// Starts watching the specified dependency.
	/// </summary>
	/// <param name="dep">Dependency to watch.</param>
	void Watch(T dep);

	/// <summary>
	/// Stops watching the specified dependency.
	/// </summary>
	/// <param name="dep">Dependency to stop watching.</param>
	void Unwatch(T dep);

	/// <summary>
	/// Called when a watched dependency changes.
	/// </summary>
	/// <remarks>
	/// May be raised from any arbitrary thread; subscribers should be prepared.
	/// </remarks>
	event Action<T> Changed;
}
