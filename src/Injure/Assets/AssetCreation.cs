// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Assets;

// ==========================================================================
// asset sourcing

/// <summary>
/// Result kind returned by asset sources.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="NotHandled"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct AssetSourceResultKind {
	/// <summary>Raw switch tag for <see cref="AssetSourceResultKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// The source does not provide the requested asset.
		/// </summary>
		NotHandled,

		/// <summary>
		/// The source successfully provided a stream for the requested asset.
		/// </summary>
		Success,
	}
}

/// <summary>
/// Result returned by an asset source.
/// </summary>
/// <param name="Kind">Result kind.</param>
/// <param name="Stream">On success, stream containing the source data.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a
/// <see cref="AssetSourceResultKind.NotHandled"/> result.
/// </remarks>
public readonly record struct AssetSourceResult(
	AssetSourceResultKind Kind,
	Stream? Stream = null
) {
	/// <summary>
	/// Factory for a <see cref="AssetSourceResultKind.NotHandled"/> result.
	/// </summary>
	public static AssetSourceResult NotHandled() => new(AssetSourceResultKind.NotHandled);

	/// <summary>
	/// Factory for a <see cref="AssetSourceResultKind.Success"/> result.
	/// </summary>
	public static AssetSourceResult Success(Stream stream) => new(AssetSourceResultKind.Success, stream);
}

/// <summary>
/// Information passed to an asset source.
/// </summary>
/// <param name="AssetId">Asset ID being requested.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct AssetSourceInfo(
	AssetId AssetId
);

/// <summary>
/// Provides raw source streams for requested asset IDs.
/// </summary>
/// <remarks>
/// <para>
/// Returning <c>NotHandled</c> means the next source should be tried; sources should return it for
/// assets they do not provide. Failures, including recognized-but-unreadable data, are reported by
/// throwing; the store wraps the exception in an <see cref="AssetLoadException"/>. On success,
/// stream ownership transfers to the asset pipeline.
/// </para>
/// <para>
/// Implementations must not call into the asset store that owns them: getting, borrowing, pinning,
/// warming, or reloading assets from inside the pipeline can deadlock, or recurse without end.
/// </para>
/// </remarks>
public interface IAssetSource {
	/// <summary>
	/// Attempts to open source data for an asset.
	/// </summary>
	/// <param name="info">Source request information.</param>
	/// <param name="coll">Dependency collector for dependencies discovered by this source.</param>
	/// <param name="ct">Cancellation token.</param>
	ValueTask<AssetSourceResult> TrySourceAsync(AssetSourceInfo info, IAssetDepCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// asset creation

/// <summary>
/// Result kind returned by asset creators or the prepare stage of staged asset creators.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="NotHandled"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct AssetCreateResultKind {
	/// <summary>Raw switch tag for <see cref="AssetCreateResultKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// The creator doesn't recognize the asset's data; the next creator is tried.
		/// </summary>
		NotHandled,

		/// <summary>
		/// The creator successfully produced the asset or, for staged creators, the prepared data for the
		/// finalize stage.
		/// </summary>
		Success,
	}
}

/// <summary>
/// Result returned by a direct asset creator.
/// </summary>
/// <typeparam name="T">Type of the asset value the creator produces.</typeparam>
/// <param name="Kind">Result kind.</param>
/// <param name="Value">On success, the created asset value.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a
/// <see cref="AssetCreateResultKind.NotHandled"/> result.
/// </remarks>
public readonly record struct AssetCreateResult<T>(
	AssetCreateResultKind Kind,
	T? Value = null
) where T : class {
	/// <summary>
	/// Factory for a <see cref="AssetCreateResultKind.NotHandled"/> result.
	/// </summary>
	public static AssetCreateResult<T> NotHandled() => new(AssetCreateResultKind.NotHandled);

	/// <summary>
	/// Factory for a <see cref="AssetCreateResultKind.Success"/> result.
	/// </summary>
	public static AssetCreateResult<T> Success(T value) => new(AssetCreateResultKind.Success, value);
}

/// <summary>
/// Information and data access passed to an asset creator, or to the prepare stage of a staged one.
/// </summary>
/// <param name="AssetId">ID of the asset being created.</param>
/// <param name="OpenAsync">
/// Opens the asset's own data, as provided by the asset sources. Each call returns it positioned at
/// its start; the stream is seekable and read-only, and owned by the store, which shares it between
/// the creators it tries and disposes it once they're done, so it's only valid during the create or
/// prepare call. Throws <see cref="AssetUnhandledException"/> if no source provides the asset;
/// creators of assets that don't need any data can just not call it.
/// </param>
/// <param name="FetchAsync">
/// Fetches the data of another asset ID, e.g. an image a metadata file refers to. Throws
/// <see cref="AssetUnhandledException"/> if no source provides it.
/// </param>
/// <param name="TryFetchAsync">
/// Fetches the data of another asset ID, or returns <see langword="null"/> if no source provides
/// it.
/// </param>
/// <remarks>
/// <para>
/// Streams from <paramref name="FetchAsync"/> and <paramref name="TryFetchAsync"/> are seekable and
/// owned by the creator, which must dispose them. The dependencies collected while fetching them
/// belong to the asset only if the creator succeeds; those of <paramref name="OpenAsync"/> always
/// do.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly record struct AssetCreateInfo(
	AssetId AssetId,
	Func<CancellationToken, ValueTask<Stream>> OpenAsync,
	Func<AssetId, CancellationToken, ValueTask<Stream>> FetchAsync,
	Func<AssetId, CancellationToken, ValueTask<Stream?>> TryFetchAsync
);

/// <summary>
/// Creates asset values of one type from their data.
/// </summary>
/// <typeparam name="T">Type of the asset values this creator produces.</typeparam>
/// <remarks>
/// <para>
/// The creators registered for a type are tried in order until one returns
/// <see cref="AssetCreateResultKind.Success"/>. Failures, including recognized-but-unreadable data,
/// are reported by throwing; the store wraps the exception in an <see cref="AssetLoadException"/>.
/// </para>
/// <para>
/// Implementations must not call into the asset store that owns them: getting, borrowing, pinning,
/// warming, or reloading assets from inside the pipeline can deadlock, or recurse without end.
/// </para>
/// </remarks>
public interface IAssetCreator<T> where T : class {
	/// <summary>
	/// Attempts to create an asset value.
	/// </summary>
	/// <param name="info">Create request information.</param>
	/// <param name="coll">Dependency collector for dependencies discovered by this creator.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <remarks>
	/// The returned value should be treated as owned by the asset store after success.
	/// </remarks>
	ValueTask<AssetCreateResult<T>> TryCreateAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct = default);
}

// ==========================================================================
// staged asset creation

/// <summary>
/// Information passed to the finalize stage of a staged asset creator.
/// </summary>
/// <typeparam name="TPrepared">Prepared-data type consumed by finalization.</typeparam>
/// <param name="AssetId">Asset ID being finalized.</param>
/// <param name="Prepared">Prepared data produced by the finalize stage.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct AssetFinalizeInfo<TPrepared>(
	AssetId AssetId,
	TPrepared Prepared
) where TPrepared : AssetPreparedData;

/// <summary>
/// Result returned by the prepare stage of a staged asset creator.
/// </summary>
/// <typeparam name="TPrepared">Prepared-data type produced by the prepare stage.</typeparam>
/// <param name="Kind">Result kind.</param>
/// <param name="Prepared">On success, the prepared data.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a
/// <see cref="AssetCreateResultKind.NotHandled"/> result.
/// </remarks>
public readonly record struct AssetPrepareResult<TPrepared>(
	AssetCreateResultKind Kind,
	TPrepared? Prepared = null
) where TPrepared : AssetPreparedData {
	/// <summary>Factory for a <see cref="AssetCreateResultKind.NotHandled"/> result.</summary>
	public static AssetPrepareResult<TPrepared> NotHandled() => new(AssetCreateResultKind.NotHandled);

	/// <summary>Factory for a <see cref="AssetCreateResultKind.Success"/> result.</summary>
	public static AssetPrepareResult<TPrepared> Success(TPrepared prepared) => new(AssetCreateResultKind.Success, prepared);
}

/// <summary>
/// Creates asset values of one type from their data in two stages, prepare and finalize.
/// </summary>
/// <typeparam name="T">Type of the asset values this creator produces.</typeparam>
/// <typeparam name="TPrepared">
/// Prepared-data type produced by preparation and consumed by finalization.
/// </typeparam>
/// <remarks>
/// <para>
/// Useful when most work can be done asynchronously but final asset value construction must happen
/// synchronously during reload publication, for example when creating GPU resources. Creators are
/// tried and report failures as with <see cref="IAssetCreator{T}"/>.
/// </para>
/// <para>
/// Implementations must not call into the asset store that owns them: getting, borrowing, pinning,
/// warming, or reloading assets from inside the pipeline can deadlock, or recurse without end.
/// </para>
/// </remarks>
public interface IAssetStagedCreator<T, TPrepared> where T : class where TPrepared : AssetPreparedData {
	/// <summary>
	/// Attempts to prepare data for a later finalize step.
	/// </summary>
	/// <param name="info">Create request information.</param>
	/// <param name="coll">Dependency collector for dependencies discovered during preparation.</param>
	/// <param name="ct">Cancellation token.</param>
	ValueTask<AssetPrepareResult<TPrepared>> TryPrepareAsync(AssetCreateInfo info, IAssetDepCollector coll, CancellationToken ct = default);

	/// <summary>
	/// Finalizes a prepared asset value.
	/// </summary>
	/// <param name="info">Finalize request information.</param>
	/// <remarks>
	/// The asset store disposes the prepared data after this method returns or throws. If finalization
	/// transfers resources out of the prepared object, the prepared object should implement that
	/// transfer explicitly.
	/// </remarks>
	T Finalize(AssetFinalizeInfo<TPrepared> info);
}

/// <summary>
/// Intermediate prepared data produced by a staged creator for the finalize stage.
/// </summary>
/// <remarks>
/// Prepare success transfers ownership of the data object to the asset store. The store disposes it
/// after finalization succeeds/fails or when a prepared reload is superseded before publication. As
/// such, creators must not hold onto this object beyond the finalize call unless explicitly
/// supported by the specific prepared asset data type.
/// </remarks>
public abstract class AssetPreparedData() : IDisposable {
	/// <summary>
	/// Releases any resources held by this prepared data object.
	/// </summary>
	public virtual void Dispose() => GC.SuppressFinalize(this);
}
