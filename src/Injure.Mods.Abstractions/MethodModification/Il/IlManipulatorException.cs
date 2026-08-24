// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Indicates that a manipulator's callback threw.
/// </summary>
/// <remarks>
/// The original exception is the inner exception, unchanged. This wrapper exists so that a thrown
/// exception can be attributed to an owner without re-running the pipeline, since otherwise a
/// manipulator that throws is indistinguishable from any other exception that managed to escape
/// from <c>Transform</c>.
/// </remarks>
public sealed class IlManipulatorException : IlPipelineException {
	public string OwnerId { get; }
	public string LocalId { get; }

	internal IlManipulatorException(string ownerId, string localId, Exception inner)
		: base($"manipulator '{ownerId}::{localId}' threw: {inner.Message}", inner) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		InternalStateException.ThrowIfInvalidLocalId(localId);
		OwnerId = ownerId;
		LocalId = localId;
	}
}
