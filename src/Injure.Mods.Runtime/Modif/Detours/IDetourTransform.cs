// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Profiler;

namespace Injure.Mods.Runtime.Modif.Detours;

internal interface IDetourTransform {
	/// <summary>
	/// Rebuilds a method's chain from its current detours.
	/// </summary>
	/// <remarks>
	/// Called on every change to the detour set, including ones that leave the emitted IL untouched.
	/// An empty set clears the slot rather than installing an empty chain, so a prologue that outlives
	/// its detours falls through to the original body.
	/// </remarks>
	public void UpdateChain(MethodIdentity method, ImmutableArray<DetourRegistration> detours);

	/// <summary>
	/// Whether a chain is currently installed for a method.
	/// </summary>
	/// <remarks>
	/// For tests to assert that every prologue has a populated chain behind it, since if it doesn't,
	/// a detoured method may get a valid prologue referencing an allocated-but-empty slot.
	/// </remarks>
	public bool HasChain(MethodIdentity method);

	/// <remarks>
	/// <paramref name="body"/> is shared with the cache, so it must be cloned in order to be edited.
	/// </remarks>
	IlMethodBody Apply(MethodIdentity method, IlMethodBody body, ImmutableArray<DetourRegistration> detours);
}
