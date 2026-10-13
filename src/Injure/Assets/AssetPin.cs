// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Assets;

/// <summary>
/// A pinned asset version, kept alive until the pin is disposed.
/// </summary>
/// <typeparam name="T">Asset type. Must be a reference type.</typeparam>
/// <remarks>
/// <para>
/// A pin always refers to the same version, even after reloads publish newer ones; check
/// <see cref="IsCurrent"/> to find out if it's out of date, and unpin/repin to move to the current
/// version. While pinned, a replaced version isn't reclaimed, which for e.g. textures means the GPU
/// memory stays in use, so pins should be disposed once they're not needed. Leaking a pin is the
/// same hazard as any other resource leak.
/// </para>
/// <para>
/// Unlike borrows, pins aren't tied to a thread: they can be created, used, and disposed from any
/// thread, attached to the store or not. Disposing the last pin of a replaced version doesn't
/// reclaim it right away; the next <see cref="AssetStore.ApplyQueuedReloads()"/> does.
/// </para>
/// </remarks>
public sealed class AssetPin<T> : IDisposable where T : class {
	private readonly VersionSlot<T> slot;
	private readonly AssetStore.AssetVersion<T> version;
	private int disposed = 0;

	internal AssetPin(VersionSlot<T> slot, AssetStore.AssetVersion<T> version) {
		this.slot = slot;
		this.version = version;
	}

	/// <summary>
	/// The pinned value.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this pin has been disposed.
	/// </exception>
	public T Value => chk().Value;

	/// <summary>
	/// The pinned version's version number. Version numbers start at 1 and monotonically increment
	/// with each new version. Never 0.
	/// </summary>
	/// <inheritdoc cref="Value" path="/exception"/>
	public ulong Version => chk().Version;

	/// <summary>
	/// The dependencies collected while creating the pinned version.
	/// </summary>
	/// <inheritdoc cref="Value" path="/exception"/>
	public ImmutableArray<IAssetDep> Deps => chk().Deps;

	/// <summary>
	/// Whether the pinned version is still the current version. For a <see cref="DerivedAsset{T}"/>,
	/// that also means none of its inputs got a new version since.
	/// </summary>
	/// <inheritdoc cref="Value" path="/exception"/>
	public bool IsCurrent => slot.IsCurrent(chk());

	/// <summary>
	/// Releases the pin, allowing the version to be reclaimed once it's replaced.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		version.ReleasePin();
	}

	private AssetStore.AssetVersion<T> chk() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		return version;
	}
}
