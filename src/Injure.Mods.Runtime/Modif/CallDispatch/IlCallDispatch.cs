// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions.Modif.Il;

namespace Injure.Mods.Runtime.Modif.CallDispatch;

internal sealed class IlCallDispatch : IIlCallDispatch {
	public static IlCallDispatch Instance { get; } = new();

	private IlCallDispatch() {
	}

	public IlMethodRef ResolveTarget { get; } = IlRefFactory.Method(
		typeof(IndirectCallDispatch).GetMethod(nameof(IndirectCallDispatch.GetTarget), BindingFlags.Static | BindingFlags.Public)!
	);

	public int AllocateSlot(MethodInfo target) {
		InternalStateException.ThrowIfNull(target);
		return IndirectCallDispatch.AllocateSlot(target);
	}
}
