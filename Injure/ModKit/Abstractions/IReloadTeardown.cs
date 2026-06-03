// SPDX-License-Identifier: MIT

using System;

using Injure.Internals.Analyzers.Attributes;
using Injure.ModKit.Abstractions.CodeAnalysis;

namespace Injure.ModKit.Abstractions;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ReloadTeardownReason {
	public enum Case {
		Reload = 1,
		Disable,
		Shutdown,
		Abort,
		FailureRollback,
		PartialReload,
	}
}

public readonly record struct ReloadTeardownContext(
	string OwnerID,
	ReloadGeneration OldGeneration,
	ReloadTeardownReason Reason
);

/// <summary>
/// Represents state that must be torn down when a reload generation ends.
/// </summary>
/// <remarks>
/// <para>
/// Intended for registrations, entries, subscriptions, cache records, and similar state that
/// needs to be removed when a reload generation ends, but does not itself own any unmanaged or
/// <see cref="IDisposable"/>-like resources. Informally speaking, this is for state that
/// could safely live forever if it belonged to the engine, game, or a non-reloadable mod, but
/// needs to be cleared for reloadable mods.
/// </para>
/// </remarks>
[Obligation]
public interface IReloadTeardown {
	/// <summary>
	/// Tears down object-defined previously established state.
	/// </summary>
	/// <param name="ctx">Context describing the teardown reason and generation; primarily for diagnostics/debugging.</param>
	/// <remarks>
	/// Implementations must be idempotent, not depend on ordering relative to other
	/// <see cref="Teardown(in ReloadTeardownContext)"/> calls, and may run on any arbitrary thread.
	/// </remarks>
	[SatisfiesObjectObligation(ObligationSatisfactionLevel.Method)]
	void Teardown(in ReloadTeardownContext ctx);
}
