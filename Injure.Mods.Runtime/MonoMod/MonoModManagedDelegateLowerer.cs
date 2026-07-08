// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Injure.Mods.Abstractions.Hooks.Il;

namespace Injure.Mods.Runtime.MonoMod;

internal sealed class MonoModManagedDelegateLowerer(ILContext ctx) : IIlManagedDelegateLowerer {
	private static readonly MethodInfo emitDelegateMethod = findEmitDelegateMethod();
	private ILContext? ctx = ctx;

	public IlManagedDelegateLowering Lower(in IlManagedDelegateLoweringRequest request) {
		ILContext curr = ctx ?? throw new InternalStateException("monomod managed-delegate lowerer was used after its strong refs were dropped");

		Delegate callback = request.Callback;
		int start = curr.Instrs.Count;
		ILCursor cursor = new(curr) {
			Index = start,
		};

		MethodInfo emitMethod = emitDelegateMethod.MakeGenericMethod(callback.GetType());
		try {
			emitMethod.Invoke(cursor, [callback]);
		} catch (TargetInvocationException ex) when (ex.InnerException is not null) {
			throw new InternalStateException(
				$"monomod ILCursor.EmitDelegate failed while lowering managed delegate for '{request.OwnerId}::{request.LocalId}'",
				ex.InnerException
			);
		}

		int count = curr.Instrs.Count - start;
		if (count <= 0)
			throw new InternalStateException("monomod ILCursor.EmitDelegate produced no instructions");

		var instrs = new Instruction[count];
		for (int i = 0; i < count; i++)
			instrs[i] = curr.Instrs[start + i];
		for (int i = 0; i < count; i++)
			curr.Instrs.RemoveAt(start);

		return new IlManagedDelegateLowering(instrs, new Retention<Delegate>(callback));
	}

	public void DropStrongReferences() {
		ctx = null;
	}

	private static MethodInfo findEmitDelegateMethod() {
		MethodInfo[] candidates = typeof(ILCursor).GetMethods(BindingFlags.Instance | BindingFlags.Public);
		foreach (MethodInfo m in candidates) {
			if (
				m.Name == nameof(ILCursor.EmitDelegate) &&
				m.IsGenericMethodDefinition &&
				m.GetParameters().Length == 1
			)
				return m;
		}
		throw new InternalStateException("couldn't find monomod ILCursor.EmitDelegate<T>(T)");
	}
}
