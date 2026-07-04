// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Assets;

/// <summary>
/// Exception thrown when an engine resource load operation fails.
/// </summary>
public sealed class EngineResourceException : Exception {
	/// <summary>
	/// ID of the engine resource involved in the failed operation.
	/// </summary>
	public EngineResourceId ResourceId { get; }

	public EngineResourceException(EngineResourceId resourceId, string message) : base($"{resourceId}: {message}") {
		ResourceId = resourceId;
	}

	public EngineResourceException(EngineResourceId resourceId, string message, Exception ex) : base($"{resourceId}: {message}", ex) {
		ResourceId = resourceId;
	}
}
