// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Injure.CodeAnalysis.Internal;
using Injure.Mods;

namespace Injure.Assets;

/// <summary>
/// Non-generic view of an <see cref="Asset{T}"/>, for bulk operations and generic tooling.
/// </summary>
[DontImplement]
public interface IUntypedAsset {
	/// <inheritdoc cref="Asset{T}.AssetType"/>
	Type AssetType { get; }

	/// <inheritdoc cref="Asset{T}.AssetId"/>
	AssetId AssetId { get; }

	/// <inheritdoc cref="Asset{T}.IsLoaded"/>
	bool IsLoaded { get; }

	/// <inheritdoc cref="Asset{T}.HasQueuedReload"/>
	bool HasQueuedReload { get; }

	/// <inheritdoc cref="Asset{T}.LastLoadException"/>
	ExceptionSnapshot? LastLoadException { get; }

	/// <inheritdoc cref="Asset{T}.LastReloadFailure"/>
	AssetReloadFailure? LastReloadFailure { get; }

	/// <inheritdoc cref="Asset{T}.WarmAsync(CancellationToken)"/>
	Task WarmAsync(CancellationToken ct = default);

	/// <inheritdoc cref="Asset{T}.QueueReloadAsync(CancellationToken)"/>
	Task QueueReloadAsync(CancellationToken ct = default);

	/// <inheritdoc cref="Asset{T}.Warm(CancellationToken)"/>
	void Warm(CancellationToken ct = default);

	/// <inheritdoc cref="Asset{T}.QueueReload(CancellationToken)"/>
	void QueueReload(CancellationToken ct = default);

	/// <inheritdoc cref="Asset{T}.Evict()"/>
	bool Evict();
}

/// <summary>
/// A logical, hot-reloadable asset of type <typeparamref name="T"/> in an <see cref="AssetStore"/>.
/// </summary>
/// <typeparam name="T">Asset type. Must be a reference type.</typeparam>
/// <remarks>
/// <para>
/// An <see cref="Asset{T}"/> can be thought of as a representation of the logical concept of an
/// "asset". For example, an image file in an assets directory can become completely different in
/// terms of its contents, but logically it's still the same asset. Assets don't necessarily
/// correspond to on-disk files: an asset source can serve assets from disk, embedded assembly
/// resources, an HTTP server, a C# dictionary, generate them on the fly, etc.
/// </para>
/// <para>
/// This is a handle that stands for such an asset, with a notion of a <i>version</i> and the
/// <i>current version</i>, where a reload creates a new version and advances the current version to
/// it. So, for example, an on-disk image file that an asset was just created from would be on
/// version 1, and editing it in an image editor and saving would reload the asset to version 2.
/// There is deliberately no version 0, so that 0 can be safely used as a sentinel value.
/// </para>
/// <para>
/// Hot reload doesn't mean that the contents can change under your nose at any time. A thread
/// <i>attaches</i> to an asset store, and can report an <i>asset reload boundary</i>, when it's at
/// a point where nothing in particular is going on and old asset versions can be reclaimed. This is
/// what keeps things like "hot-reloadable shader" sane, and is documented in more detail at
/// <see cref="AssetStore"/>.
/// </para>
/// <para>
/// There are two ways to access the current version. The standard way is to <i>borrow</i> the
/// asset: <see cref="Borrow()"/> gives access to the current version through an
/// <see cref="AssetBorrow{T}"/>, which is valid until the borrowing thread's next asset reload
/// boundary. If the version must be held onto for longer / with a more controllable lifetime, or a
/// thread doesn't want to or can't attach to the asset store, an alternative is to <i>pin</i> the
/// asset: <see cref="Pin()"/> gives an <see cref="AssetPin{T}"/> that keeps that version alive
/// until the pin is disposed.
/// </para>
/// <para>
/// Plenty of Injure APIs take in <see cref="Asset{T}"/> values directly, so explicit
/// borrowing/pinning is oftentimes not even necessary.
/// </para>
/// </remarks>
public sealed class Asset<T> : Versioned<T>, IUntypedAsset, IAssetSlotAccess where T : class {
	internal readonly AssetStore.AssetSlot<T> AssetSlot;
	internal ulong SlotId => AssetSlot.SlotId;
	internal Asset(AssetStore.AssetSlot<T> slot) {
		AssetSlot = slot;
	}

	internal override VersionSlot<T> Slot => AssetSlot;
	AssetStore.IAssetSlot IAssetSlotAccess.UntypedAssetSlot => AssetSlot;

	/// <summary>
	/// The asset type, i.e. <typeparamref name="T"/>.
	/// </summary>
	public Type AssetType => typeof(T);

	/// <summary>
	/// The asset ID.
	/// </summary>
	public AssetId AssetId => AssetSlot.AssetId;

	/// <summary>
	/// Whether a current version exists.
	/// </summary>
	public bool IsLoaded => AssetSlot.HasCurrent;

	/// <summary>
	/// Whether a prepared replacement version is waiting for
	/// <see cref="AssetStore.ApplyQueuedReloads()"/>.
	/// </summary>
	public bool HasQueuedReload => AssetSlot.HasQueuedReload;

	/// <summary>
	/// The exception of the last failed initial load, if any.
	/// </summary>
	/// <remarks>
	/// Reload failures are reported separately, through <see cref="LastReloadFailure"/>.
	/// </remarks>
	public ExceptionSnapshot? LastLoadException => AssetSlot.LastLoadException;

	/// <summary>
	/// The last reload failure, if any; cleared by a successful reload.
	/// </summary>
	public AssetReloadFailure? LastReloadFailure => AssetSlot.LastReloadFailure;

	/// <summary>
	/// Borrows the current version, loading it first if there isn't one. Requires the calling thread
	/// to be attached to the store.
	/// </summary>
	/// <returns>A borrow of the current version.</returns>
	/// <remarks>
	/// Blocks while loading the first version if there isn't one.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the calling thread isn't attached to the store; see
	/// <see cref="AssetStore.AttachCurrentThread()"/>.
	/// </exception>
	/// <exception cref="AssetLoadException">
	/// Thrown if there's no current version and loading the first one fails.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public override AssetBorrow<T> Borrow() => AssetSlot.Borrow();

	/// <summary>
	/// Borrows the current version if there is one, without loading anything. Requires the calling
	/// thread to be attached to the store.
	/// </summary>
	/// <param name="borrow">On success, a borrow of the current version.</param>
	/// <returns>
	/// Whether there's a current version (that was subsequently successfully borrowed).
	/// </returns>
	/// <remarks>
	/// Succeeds after a successful <see cref="WarmAsync(CancellationToken)"/>, unless
	/// <see cref="Evict()"/> was called since.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the calling thread isn't attached to the store; see
	/// <see cref="AssetStore.AttachCurrentThread()"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public bool TryPassiveBorrow(out AssetBorrow<T> borrow) => AssetSlot.TryPassiveBorrow(out borrow);

	/// <summary>
	/// Borrows the current version, without loading anything. Requires the calling thread to be
	/// attached to the store.
	/// </summary>
	/// <returns>A borrow of the current version.</returns>
	/// <remarks>
	/// Succeeds after a successful <see cref="WarmAsync(CancellationToken)"/>, unless
	/// <see cref="Evict()"/> was called since.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if there's no current version, or if the calling thread isn't attached to the store; see
	/// <see cref="AssetStore.AttachCurrentThread()"/> for the latter.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public AssetBorrow<T> PassiveBorrow() {
		if (!AssetSlot.TryPassiveBorrow(out AssetBorrow<T> borrow))
			throw new InvalidOperationException($"asset '{AssetId}' has no current version");
		return borrow;
	}

	/// <summary>
	/// Pins the current version, loading it first if there isn't one. Works from any thread, attached
	/// to the store or not.
	/// </summary>
	/// <returns>A pin of the current version, owned by the caller.</returns>
	/// <remarks>
	/// Blocks while loading the first version if there isn't one.
	/// </remarks>
	/// <exception cref="AssetLoadException">
	/// Thrown if there's no current version and loading the first one fails.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public override AssetPin<T> Pin() => AssetSlot.Pin();

	/// <summary>
	/// Pins the current version if there is one, without loading anything. Works from any thread,
	/// attached to the store or not.
	/// </summary>
	/// <param name="pin">On success, a pin of the current version, owned by the caller.</param>
	/// <returns>
	/// Whether there's a current version (that was subsequently successfully pinned).
	/// </returns>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public bool TryPassivePin([NotNullWhen(true)] out AssetPin<T>? pin) => AssetSlot.TryPassivePin(out pin);

	/// <summary>
	/// Pins the current version, without loading anything. Works from any thread, attached to the
	/// store or not.
	/// </summary>
	/// <returns>A pin of the current version, owned by the caller.</returns>
	/// <remarks>
	/// Succeeds after a successful <see cref="WarmAsync(CancellationToken)"/>, unless
	/// <see cref="Evict()"/> was called since.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if there's no current version.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public AssetPin<T> PassivePin() {
		if (!AssetSlot.TryPassivePin(out AssetPin<T>? pin))
			throw new InvalidOperationException($"asset '{AssetId}' has no current version");
		return pin;
	}

	/// <summary>
	/// Ensures a current version exists, loading it if there's none.
	/// </summary>
	/// <remarks>
	/// Concurrent calls share the same load. After success,
	/// <see cref="TryPassiveBorrow(out AssetBorrow{T})"/> succeeds until <see cref="Evict()"/>.
	/// </remarks>
	/// <exception cref="AssetLoadException">
	/// Thrown if there's no current version and loading the first one fails.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public Task WarmAsync(CancellationToken ct = default) => AssetSlot.WarmAsync(ct);

	/// <summary>
	/// Prepares a replacement version, to be published by
	/// <see cref="AssetStore.ApplyQueuedReloads()"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Concurrent reload requests are coalesced: each accepted request takes a new version number, but
	/// fewer preparations may happen. The returned task completes once the newest requested version is
	/// prepared, or fails with the exception that preparing it failed with.
	/// </para>
	/// <para>
	/// There must be a current version to replace; load the asset first, e.g. with
	/// <see cref="WarmAsync(CancellationToken)"/>.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if there's no current version.
	/// </exception>
	/// <exception cref="AssetLoadException">
	/// Thrown if preparing the replacement fails.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed, including while the replacement was being prepared.
	/// </exception>
	public Task QueueReloadAsync(CancellationToken ct = default) => AssetSlot.QueueReloadAsync(ct);

	/// <summary>
	/// Blocking version of <see cref="WarmAsync(CancellationToken)"/>.
	/// </summary>
	/// <inheritdoc cref="WarmAsync(CancellationToken)"/>
	public void Warm(CancellationToken ct = default) => WarmAsync(ct).GetAwaiter().GetResult();

	/// <summary>
	/// Blocking version of <see cref="QueueReloadAsync(CancellationToken)"/>.
	/// </summary>
	/// <inheritdoc cref="QueueReloadAsync(CancellationToken)"/>
	public void QueueReload(CancellationToken ct = default) => QueueReloadAsync(ct).GetAwaiter().GetResult();

	/// <summary>
	/// Returns the asset to its unloaded state: retires the current version, discards a prepared
	/// reload, and clears <see cref="LastLoadException"/> and <see cref="LastReloadFailure"/>.
	/// </summary>
	/// <returns>Whether there was anything to clear.</returns>
	/// <remarks>
	/// The retired version is reclaimed like a replaced one, so existing borrows and pins of it stay
	/// valid as usual. Version numbers keep counting up rather than starting over.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if a load or reload preparation is in progress.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the store has been disposed.
	/// </exception>
	public bool Evict() => AssetSlot.Evict();
}
