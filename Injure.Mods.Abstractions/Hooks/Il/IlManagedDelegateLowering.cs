// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.Hooks.Il;

internal readonly struct IlManagedDelegateLoweringRequest(MethodDefinition method, Delegate callback, string ownerId, string localId) {
	public MethodDefinition Method { get; } = method;
	public Delegate Callback { get; } = callback;
	public string OwnerId { get; } = ownerId;
	public string LocalId { get; } = localId;
}

internal sealed class IlManagedDelegateLowering : IStrongRefDroppable {
	private Instruction[] instrs;
	private IDisposable? retention;

	public IlManagedDelegateLowering(IEnumerable<Instruction> instrs, IDisposable? retention = null) {
		if (instrs is null)
			throw new InternalStateException("managed-delegate lowering has null instruction list");
		this.instrs = instrs.ToArray();
		if (this.instrs.Length == 0)
			throw new InternalStateException("managed-delegate lowering has no instructions in it");
		for (int i = 0; i < this.instrs.Length; i++)
			if (this.instrs[i] is null)
				throw new InternalStateException($"managed-delegate lowering has null instruction at idx {i}");
		this.retention = retention;
	}

	public Instruction[] TakeInstructions() => Interlocked.Exchange(ref instrs, Array.Empty<Instruction>());
	public IDisposable? TakeRetention() => Interlocked.Exchange(ref retention, null);

	public void DropStrongReferences() {
		_ = TakeInstructions();
		_ = TakeRetention();
	}
}

internal interface IIlManagedDelegateLowerer {
	IlManagedDelegateLowering Lower(in IlManagedDelegateLoweringRequest req);
}
