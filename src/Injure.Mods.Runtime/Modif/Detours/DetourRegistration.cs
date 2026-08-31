// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace Injure.Mods.Runtime.Modif.Detours;

/// <summary>
/// Desciptor of a registered detour.
/// </summary>
/// <remarks>
/// Chain-related info is stored separately; this is only information independent of a chain a
/// detour belongs to.
/// </remarks>
internal sealed class DetourRegistration {
	public string OwnerId { get; }
	public string LocalId { get; }

	/// <summary>
	/// The method the detour runs instead of the target.
	/// </summary>
	public MethodBase Impl { get; }

	public DetourRegistration(string ownerId, string localId, MethodBase impl) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		InternalStateException.ThrowIfInvalidLocalId(localId);
		InternalStateException.ThrowIfNull(impl);
		OwnerId = ownerId;
		LocalId = localId;
		Impl = impl;
	}

	public override string ToString() => $"{OwnerId}::{LocalId}";
}
