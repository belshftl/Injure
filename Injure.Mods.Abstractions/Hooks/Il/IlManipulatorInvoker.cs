// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Hooks.Il;

internal interface IIlManipulatorInvoker : IStrongRefDroppable {
	void Invoke(IlTransactionCore core);
}

internal sealed class IlManipulatorInvokerImpl<L>(IlManipulator<L> manipulator) : IIlManipulatorInvoker where L : struct, IModLifetimeIdentity {
	private IlManipulator<L>? manipulator = manipulator ?? throw new InternalStateException("IlManipulatorInvokerImpl constructed with null manipulator");
	public void Invoke(IlTransactionCore core) {
		IlManipulator<L> m = Volatile.Read(ref manipulator) ?? throw new InternalStateException("IL manipulator got invoked after strong ref to it has been dropped");
		IlContext<L> ctx = new(core);
		try {
			m(ctx);
		} finally {
			ctx.DropStrongReferences();
		}
	}
	public void DropStrongReferences() {
		Volatile.Write(ref manipulator, null);
	}
}
