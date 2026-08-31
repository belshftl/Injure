// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Per-thread attachment to an <see cref="AssetStore"/> for deferred asset reclamation tracking.
/// </summary>
public sealed class AssetThreadCtx : IDisposable {
	private static ulong nextId = 0;
	private readonly AssetStore owner;
	private int disposed = 0;

	internal ulong Id { get; }
	internal ulong QuiescentEpoch; // owner writes to here

	internal AssetThreadCtx(AssetStore owner) {
		this.owner = owner;
		Id = Interlocked.Increment(ref nextId);
	}

	/// <summary>
	/// Reports a safe boundary.
	/// </summary>
	/// <remarks>
	/// By calling this, the current thread is declaring that it is okay with any
	/// asset leases borrowed before this call being reclaimed and invalidated.
	/// </remarks>
	public void AtSafeBoundary() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		Volatile.Write(ref QuiescentEpoch, owner.GetPublishedEpoch());
		owner.TryCollectRetired();
	}

	/// <summary>
	/// Detaches this thread context from its <see cref="AssetStore"/>.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		Volatile.Write(ref QuiescentEpoch, owner.GetPublishedEpoch());
		owner.TryCollectRetired();
		owner.DetachThread(this);
	}
}
