// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Indicates that the method body produced by an IL transformation pipeline is not a valid CLI
/// method body.
/// </summary>
/// <remarks>
/// <para>
/// If <see cref="OwnerId"/> is non-<see langword="null"/>, the pipeline attributed the failure to
/// one manipulator: the body was valid before that manipulator ran and invalid after. Attribution
/// is a best-effort diagnostic, so a manipulator whose edits are only invalid in combination with a
/// later manipulator's edits is attributed to whichever one first produces an invalid body.
/// </para>
/// <para>
/// <see cref="OwnerId"/> is <see langword="null"/> if attribution was disabled, or if the
/// attribution pass did not reproduce the failure.
/// </para>
/// </remarks>
public sealed class IlPipelineValidationException : IlPipelineException {
	/// <summary>
	/// The owner whose manipulator produced the invalid body, or <see langword="null"/> if the
	/// failure was not attributed.
	/// </summary>
	public string? OwnerId { get; }

	/// <summary>
	/// The local ID of the manipulator that produced the invalid body, or
	/// <see langword="null"/> if the failure was not attributed.
	/// </summary>
	public string? LocalId { get; }

	internal IlPipelineValidationException(string message, string? ownerId, string? localId, Exception? ex) : base(fmt(message, ownerId, localId), ex!) {
		InternalStateException.ThrowIfNonnullAndInvalidOwnerId(ownerId);
		InternalStateException.ThrowIfNonnullAndInvalidLocalId(localId);
		OwnerId = ownerId;
		LocalId = localId;
	}

	private static string fmt(string message, string? ownerId, string? localId) =>
		ownerId is null ? message : $"manipulator '{ownerId}::{localId}' produced an invalid method body: {message}";
}
