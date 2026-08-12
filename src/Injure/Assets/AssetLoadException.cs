// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Base exception thrown when an asset load, prepare, or finalize operation fails.
/// </summary>
/// <remarks>
/// May be derived by user code for custom, more specific asset creation failures.
/// However, note that this exception is also used to denote failures in the pipeline as a whole
/// (see <see cref="AssetUnhandledException"/> for an example), so derive with care.
/// </remarks>
public /* open */ class AssetLoadException : Exception {
	/// <summary>
	/// ID of the asset involved in the failed operation.
	/// </summary>
	public AssetId AssetId { get; }

	/// <summary>
	/// Type of the asset involved in the failed operation.
	/// </summary>
	public Type AssetType { get; }

	public AssetLoadException(AssetId id, Type type, string message) : base(fmt(id, type, message)) {
		AssetId = id;
		AssetType = type;
	}

	public AssetLoadException(AssetId id, Type type, string message, Exception ex) : base(fmt(id, type, message), ex) {
		AssetId = id;
		AssetType = type;
	}

	private static string fmt(AssetId id, Type type, string message) =>
		type is null ? $"{id}: {message}" : $"{type.Name}({id}): {message}";
}
