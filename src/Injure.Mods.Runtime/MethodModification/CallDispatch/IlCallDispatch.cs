// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions.MethodModification.Il;

namespace Injure.Mods.Runtime.MethodModification.CallDispatch;

internal sealed class IlCallDispatch : IIlCallDispatch {
	public static IlCallDispatch Instance { get; } = new();

	private IlCallDispatch() {
	}

	public IlMethodRef ResolveTarget { get; } = IlReferenceFactory.Method(
		typeof(IndirectCallDispatch).GetMethod(nameof(IndirectCallDispatch.GetTarget), BindingFlags.Static | BindingFlags.Public)!
	);

	public int AllocateSlot(MethodInfo target) {
		InternalStateException.ThrowIfNull(target);
		return IndirectCallDispatch.AllocateSlot(target);
	}
}
