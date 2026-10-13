// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Bulk operations on <see cref="Asset{T}"/>s.
/// </summary>
public static class AssetExtensions {
	extension(IEnumerable<IUntypedAsset> assets) {
		/// <summary>
		/// Ensures a current version exists for every asset, loading them concurrently.
		/// </summary>
		/// <param name="maxConcurrency">Maximum number of concurrent loads.</param>
		/// <param name="ct">Cancellation token.</param>
		/// <remarks>
		/// <para>
		/// Concurrent calls for the same asset share the same load. After success,
		/// <see cref="Asset{T}.TryPassiveBorrow(out AssetBorrow{T})"/> succeeds for every asset until
		/// it's evicted.
		/// </para>
		/// <para>
		/// After the first failure or cancellation, no further assets are started; the call throws
		/// once the ones already running finish, so the rest may be left unloaded.
		/// </para>
		/// </remarks>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown if <paramref name="maxConcurrency"/> isn't positive.
		/// </exception>
		public async Task WarmAllAsync(int maxConcurrency = 8, CancellationToken ct = default) {
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
			ParallelOptions opts = new() { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = ct };
			await Parallel.ForEachAsync(
				assets,
				opts,
				static (asset, token) => new ValueTask(asset.WarmAsync(token))
			).ConfigureAwait(false);
		}

		/// <summary>
		/// Prepares a replacement version for every asset, concurrently.
		/// </summary>
		/// <param name="maxConcurrency">Maximum number of concurrent preparations.</param>
		/// <param name="ct">Cancellation token.</param>
		/// <remarks>
		/// <para>
		/// Each asset behaves as with <see cref="Asset{T}.QueueReloadAsync(CancellationToken)"/>.
		/// </para>
		/// <para>
		/// After the first failure or cancellation, no further assets are started; the call throws
		/// once the ones already running finish, so the rest may be left without a prepared reload.
		/// </para>
		/// </remarks>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown if <paramref name="maxConcurrency"/> isn't positive.
		/// </exception>
		public async Task QueueReloadAllAsync(int maxConcurrency = 8, CancellationToken ct = default) {
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
			ParallelOptions opts = new() { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = ct };
			await Parallel.ForEachAsync(
				assets,
				opts,
				static (asset, token) => new ValueTask(asset.QueueReloadAsync(token))
			).ConfigureAwait(false);
		}

		/// <summary>
		/// Blocking version of
		/// <see cref="WarmAllAsync(IEnumerable{IUntypedAsset}, int, CancellationToken)"/>.
		/// </summary>
		/// <inheritdoc cref="WarmAllAsync(IEnumerable{IUntypedAsset}, int, CancellationToken)"/>
		public void WarmAll(int maxConcurrency = 8, CancellationToken ct = default) =>
			assets.WarmAllAsync(maxConcurrency, ct).GetAwaiter().GetResult();

		/// <summary>
		/// Blocking version of
		/// <see cref="QueueReloadAllAsync(IEnumerable{IUntypedAsset}, int, CancellationToken)"/>.
		/// </summary>
		/// <inheritdoc cref="QueueReloadAllAsync(IEnumerable{IUntypedAsset}, int, CancellationToken)"/>
		public void QueueReloadAll(int maxConcurrency = 8, CancellationToken ct = default) =>
			assets.QueueReloadAllAsync(maxConcurrency, ct).GetAwaiter().GetResult();
	}
}
