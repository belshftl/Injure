// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Manipulates one snapshot of a method body as part of an ordered IL transformation pipeline.
/// </summary>
/// <typeparam name="L">
/// Lifetime identity of the owner; see <c>Docs/mods/lifetime-identity.md</c> for more info.
/// </typeparam>
/// <param name="ctx">
/// Transaction-scoped manipulation context.
/// </param>
/// <remarks>
/// <para>
/// Matching observes the method body as it existed when this manipulator started. Edits declared by
/// this callback are committed atomically and become visible to later manipulators when the callback
/// returns successfully; if it throws, the edits are discarded.
/// </para>
/// <para>
/// A manipulator may be invoked more than once for a single transformation and must be able to
/// tolerate that. Manipulators are heavily encouraged to avoid causing any side effects other than
/// authoring IL edits through <paramref name="ctx"/>; any side effects they do cause must be safe
/// to be caused multiple times.
/// </para>
/// </remarks>
public delegate void IlManipulator<L>(IlCtx<L> ctx) where L : struct, IModLifetimeIdentity;

internal sealed class IlManipulatorRegistration {
	private readonly Action<IlTransactionCore> invoke;
	public string OwnerId { get; }
	public string LocalId { get; }

	private IlManipulatorRegistration(string ownerId, string localId, Action<IlTransactionCore> invoke) {
		InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		InternalStateException.ThrowIfInvalidLocalId(localId);
		InternalStateException.ThrowIfNull(invoke);
		OwnerId = ownerId;
		LocalId = localId;
		this.invoke = invoke;
	}

	public static IlManipulatorRegistration Create<L>(string ownerId, string localId, IlManipulator<L> manipulator) where L : struct, IModLifetimeIdentity {
		InternalStateException.ThrowIfNull(manipulator);
		return new IlManipulatorRegistration(ownerId, localId, core => manipulator(new IlCtx<L>(core)));
	}

	public void Invoke(IlTransactionCore core) {
		InternalStateException.ThrowIfNull(core);
		invoke(core);
	}
}
