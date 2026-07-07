// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Hooks.Il;

internal sealed class IlManipulatorRegistration : IStrongRefDroppable {
	private IIlManipulatorInvoker? invoker;

	public string OwnerId { get; }
	public string LocalId { get; }

	private IlManipulatorRegistration(string ownerId, string localId, IIlManipulatorInvoker invoker) {
		OwnerId = ownerId;
		LocalId = localId;
		this.invoker = invoker ?? throw new InternalStateException("IlManipulatorRegistration constructed with null invoker");
	}

	public static IlManipulatorRegistration Create<L>(string ownerId, string localId, IlManipulator<L> manipulator) where L : struct, IModLifetimeIdentity {
		if (!ModMetadataValidation.ValidateOwnerId(ownerId, out string? e))
			throw new InternalStateException($"IL manipulator registration under bad owner ID '{ownerId}': {e}");
		if (!ModMetadataValidation.ValidateLocalId(localId, out e))
			throw new InternalStateException($"IL manipulator registration under bad owner ID '{localId}': {e}");
		if (manipulator is null)
			throw new InternalStateException("IL manipulator registration happened for a null manipulator");
		return new IlManipulatorRegistration(ownerId, localId, new IlManipulatorInvokerImpl<L>(manipulator));
	}

	public void Invoke(IlTransactionCore core) => (invoker ?? throw new InternalStateException("IL manipulator got invoked after its strong refs were dropped"))?.Invoke(core);

	public void DropStrongReferences() => Interlocked.Exchange(ref invoker, null)?.DropStrongReferences();
}
