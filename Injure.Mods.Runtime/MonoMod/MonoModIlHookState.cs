// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using MonoMod.Cil;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Runtime.MonoMod;

internal sealed class MonoModIlHookState(
	string? baselineOwnerId,
	Func<IReadOnlyList<IlManipulatorRegistration>> getSnapshot
) : IDisposable, IStrongRefDroppable {
	private string? baselineOwnerId = baselineOwnerId;
	private Func<IReadOnlyList<IlManipulatorRegistration>>? getSnapshot = getSnapshot;
	private IlPipelineResult? lastResult;
	private int disposed;

	public void Apply(ILContext il) {
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("monomod IL hook state got used after disposal");
		Func<IReadOnlyList<IlManipulatorRegistration>> currentGetSnapshot = getSnapshot ??
			throw new InternalStateException("monomod IL hook state has no invocation snapshot provider");

		IReadOnlyList<IlManipulatorRegistration> snapshot = currentGetSnapshot();
		MonoModManagedDelegateLowerer lowerer = new(il);
		IlPipelineResult result;
		try {
			result = IlPipelineRunner.Transform(il.Method, baselineOwnerId, snapshot, lowerer, MonoModOperandNormalizer.Instance);
		} finally {
			lowerer.DropStrongReferences();
		}

		publishResult(result);
	}

	private void publishResult(IlPipelineResult result) {
		InternalStateException.ThrowIfNull(result);
		IlPipelineResult? r = Interlocked.Exchange(ref lastResult, result);
		r?.Dispose();
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;
		Interlocked.Exchange(ref lastResult, null)?.Dispose();
	}

	public void DropStrongReferences() {
		Dispose();
		baselineOwnerId = null;
		getSnapshot = null;
	}
}
