// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Thrown when loading, preparing, or finalizing an asset fails.
/// </summary>
/// <remarks>
/// Only the asset store throws this. Pipeline components (sources and creators) report failures by
/// throwing whatever exception fits, and the store wraps it in an <see cref="AssetLoadException"/>
/// with that exception as <see cref="Exception.InnerException"/>.
/// <see cref="AssetUnhandledException"/> is the one more specific kind.
/// </remarks>
public /* open */ class AssetLoadException : Exception {
	/// <summary>
	/// ID of the asset that failed to load.
	/// </summary>
	public AssetId AssetId { get; }

	/// <summary>
	/// Type of the asset that failed to load.
	/// </summary>
	public Type AssetType { get; }

	// internal rather than private protected, since the store constructs it too; either way nothing
	// outside the assembly can derive from it
	internal AssetLoadException(AssetId id, Type type, string message) : base(fmt(id, type, message)) {
		AssetId = id;
		AssetType = type;
	}

	internal AssetLoadException(AssetId id, Type type, string message, Exception ex) : base(fmt(id, type, message), ex) {
		AssetId = id;
		AssetType = type;
	}

	private static string fmt(AssetId id, Type type, string message) => $"{type.Name}({id}): {message}";
}
