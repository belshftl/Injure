// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// A thread's attachment to an <see cref="AssetStore"/>, which lets it borrow assets and report
/// asset reload boundaries.
/// </summary>
/// <remarks>
/// Created by <see cref="AssetStore.AttachCurrentThread()"/>, and only usable from the thread that
/// created it.
/// </remarks>
public sealed class AssetThreadAttachment : IDisposable {
	private static ulong nextId = 0;
	private readonly int threadId = Environment.CurrentManagedThreadId;
	private int disposed = 0;

	internal ulong Id { get; }
	internal AssetStore Store { get; }
	internal ulong QuiescentEpoch; // read by the thread applying reloads
	// only touched by the attached thread; borrows compare against it
	internal ulong BoundaryCount { get; private set; }

	internal AssetThreadAttachment(AssetStore store) {
		Store = store;
		Id = Interlocked.Increment(ref nextId);
	}

	/// <summary>
	/// Reports an asset reload boundary: this thread holds no borrows from before this call.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Borrows this thread made before the call become invalid, and versions they referred to may be
	/// reclaimed once every other attached thread reported a boundary too. Observing reloads doesn't
	/// need this call, since borrows always get the current version.
	/// </para>
	/// <para>
	/// This doesn't reclaim anything itself; <see cref="AssetStore.ApplyQueuedReloads()"/> does.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the attached one.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this attachment has been disposed.
	/// </exception>
	public void AtReloadBoundary() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		chkThread();
		BoundaryCount++;
		Volatile.Write(ref QuiescentEpoch, Store.GetPublishedEpoch());
	}

	/// <summary>
	/// Detaches this thread from its <see cref="AssetStore"/>, invalidating its borrows.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the attached one.
	/// </exception>
	public void Dispose() {
		if (Volatile.Read(ref disposed) != 0)
			return;
		chkThread();
		Volatile.Write(ref disposed, 1);
		BoundaryCount++;
		Store.DetachThread(this);
	}

	private void chkThread() {
		if (Environment.CurrentManagedThreadId != threadId)
			throw new InvalidOperationException(
				"an asset thread attachment can only be used from the thread that created it; if you're using tasks, an await doesn't necessarily resume on the same thread, so use a dedicated thread"
			);
	}
}
