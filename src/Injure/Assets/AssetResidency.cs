// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

// lets residencies reach the slot behind a handle; only Asset<T> implements IUntypedAsset
internal interface IAssetSlotAccess {
	AssetStore.IAssetSlot UntypedAssetSlot { get; }
}

/// <summary>
/// Keeps the members of an <see cref="AssetGroup"/> loaded while it's alive; the untyped base of
/// <see cref="AssetResidency{TGroup}"/>.
/// </summary>
/// <remarks>
/// <para>
/// An asset stays loaded while any residency includes it, and is evicted after the last one is
/// disposed. Eviction happens at the next <see cref="AssetStore.ApplyQueuedReloads()"/> and waits
/// for a load still in progress. Assets that were never resident aren't affected.
/// </para>
/// <para>
/// A residency keeps assets loaded, not a particular version of them; reloads still replace
/// versions as usual. Use <see cref="AssetPin{T}"/> to keep a version.
/// </para>
/// <para>
/// Borrowing/pinning a non-resident asset works perfectly fine; it's just that the borrow/pin takes
/// time to load it if it's not loaded yet. A residency is a promise that this doesn't happen for
/// the assets in it, and is an optional feature.
/// </para>
/// </remarks>
public abstract class AssetResidency : IDisposable {
	private readonly AssetStore.IAssetSlot[] slots;
	private int disposed = 0;

	private protected AssetResidency(AssetGroup group, AssetStore.IAssetSlot[] slots) {
		Group = group;
		this.slots = slots;
	}

	/// <summary>
	/// The group whose members this keeps loaded.
	/// </summary>
	public AssetGroup Group { get; }

	/// <summary>
	/// Whether this residency has been disposed.
	/// </summary>
	public bool IsDisposed => Volatile.Read(ref disposed) != 0;

	/// <summary>
	/// Releases the residency; members no other residency includes are evicted by the next
	/// <see cref="AssetStore.ApplyQueuedReloads()"/>.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		foreach (AssetStore.IAssetSlot slot in slots)
			slot.ReleaseResident();
		GC.SuppressFinalize(this);
	}
}

/// <summary>
/// Keeps the members of a <typeparamref name="TGroup"/> loaded while it's alive; see
/// <see cref="AssetResidency"/>.
/// </summary>
/// <typeparam name="TGroup">Type of the group.</typeparam>
/// <remarks>
/// The generic form is useful for taking in as a parameter: being provided an
/// <see cref="AssetResidency{TGroup}"/> implies that <typeparamref name="TGroup"/>'s assets are
/// loaded, and <see cref="Group"/> gives access to them.
/// </remarks>
public sealed class AssetResidency<TGroup> : AssetResidency where TGroup : AssetGroup {
	internal AssetResidency(TGroup group, AssetStore.IAssetSlot[] slots) : base(group, slots) {
	}

	/// <summary>
	/// The group whose members this keeps loaded.
	/// </summary>
	public new TGroup Group => (TGroup)base.Group;
}

/// <summary>
/// Makes <see cref="AssetGroup"/>s resident.
/// </summary>
public static class AssetGroupExtensions {
	/// <summary>
	/// Loads every member of <paramref name="group"/> that isn't loaded yet, and keeps them loaded
	/// until the returned residency is disposed.
	/// </summary>
	/// <typeparam name="TGroup">Type of the group.</typeparam>
	/// <param name="group">The group.</param>
	/// <param name="progress">
	/// If not <see langword="null"/>, receives the fraction of members loaded so far, from 0 to 1,
	/// possibly from several threads.
	/// </param>
	/// <param name="maxConcurrency">Maximum number of concurrent loads.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The residency, owned by the caller.</returns>
	/// <remarks>
	/// <para>
	/// The members are snapshotted now. Making a group resident whose members are already loaded, e.g.
	/// a subdirectory of a resident directory, completes right away.
	/// </para>
	/// <para>
	/// If loading a member fails or the call is cancelled, no further members are started, the
	/// residency isn't created, and the call throws once the loads already running finish.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="group"/> or one of its members is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="maxConcurrency"/> isn't positive.
	/// </exception>
	/// <exception cref="AssetLoadException">
	/// Thrown if loading a member fails.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if a member's store has been disposed.
	/// </exception>
	public static async Task<AssetResidency<TGroup>> MakeResidentAsync<TGroup>(
		this TGroup group,
		IProgress<float>? progress = null,
		int maxConcurrency = 8,
		CancellationToken ct = default
	) where TGroup : AssetGroup {
		ArgumentNullException.ThrowIfNull(group);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
		IUntypedAsset[] members = group.Members
			.Select(static m => m ?? throw new ArgumentNullException(nameof(group), "asset group members must not be null"))
			.Distinct(ReferenceEqualityComparer.Instance)
			.Cast<IUntypedAsset>()
			.ToArray();
		AssetStore.IAssetSlot[] slots = members.Select(static m => ((IAssetSlotAccess)m).UntypedAssetSlot).ToArray();

		// resident before loading, so a concurrent release of another residency can't evict them
		// in between
		foreach (AssetStore.IAssetSlot slot in slots)
			slot.AddResident();
		try {
			int done = 0;
			ParallelOptions opts = new() { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = ct };
			await Parallel.ForEachAsync(members, opts, async (m, token) => {
					await m.WarmAsync(token).ConfigureAwait(false);
					progress?.Report((float)Interlocked.Increment(ref done) / members.Length);
				}
			).ConfigureAwait(false);
			if (members.Length == 0)
				progress?.Report(1f);
		} catch {
			foreach (AssetStore.IAssetSlot slot in slots)
				slot.ReleaseResident();
			throw;
		}
		return new AssetResidency<TGroup>(group, slots);
	}

	/// <summary>
	/// Blocking version of
	/// <see cref="MakeResidentAsync{TGroup}(TGroup, IProgress{float}?, int, CancellationToken)"/>.
	/// </summary>
	/// <inheritdoc cref="MakeResidentAsync{TGroup}(TGroup, IProgress{float}?, int, CancellationToken)"/>
	public static AssetResidency<TGroup> MakeResident<TGroup>(
		this TGroup group,
		IProgress<float>? progress = null,
		int maxConcurrency = 8,
		CancellationToken ct = default
	) where TGroup : AssetGroup =>
		group.MakeResidentAsync(progress, maxConcurrency, ct).GetAwaiter().GetResult();
}
