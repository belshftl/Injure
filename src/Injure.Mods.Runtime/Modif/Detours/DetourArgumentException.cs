// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Runtime.Modif.Detours;

/// <summary>
/// Indicates that a detour declared with a supertype of a parameter type on the original method
/// passed a value to <c>next</c> whose type cannot be downcast back to the original type.
/// </summary>
/// <remarks>
/// Only reachable when a detour declares a parameter more general than the target's. When
/// <c>SomePrivateClass</c> is widened to <c>object</c> in the signature, nothing stops it from
/// passing in a value of a different type, and the mismatch can only be caught when the value is
/// downcast back.
/// </remarks>
public sealed class DetourArgumentException : Exception {
	/// <summary>
	/// The owner whose detour passed the bad value.
	/// </summary>
	public string OwnerId { get; }

	/// <summary>
	/// The local ID of that detour.
	/// </summary>
	public string LocalId { get; }

	/// <summary>
	/// Which parameter, counting the instance as the first where there is one.
	/// </summary>
	public int ParameterIndex { get; }

	internal DetourArgumentException(string ownerId, string localId, int index, Type expected, object? actual)
		: base(
			$"detour '{ownerId}::{localId}' passed a value of type {actual?.GetType().ToString() ?? "<null>"} to `next` as parameter {index}, which cannot be downcast back to the expected {expected}"
		) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		InternalStateException.ThrowIfInvalidOwnerId(localId);
		OwnerId = ownerId;
		LocalId = localId;
		ParameterIndex = index;
	}
}
