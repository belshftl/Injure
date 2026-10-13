// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Thrown when no registered source provides an asset's data, or no registered creator accepts it.
/// </summary>
public sealed class AssetUnhandledException : AssetLoadException {
	internal AssetUnhandledException(AssetId id, Type type, string message) : base(id, type, message) {
	}
}
