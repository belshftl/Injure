// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using MonoMod.RuntimeDetour;
using Injure.Mods.Abstractions.MethodModification;

namespace Injure.Mods.Runtime.MonoMod;

internal sealed class MonoModManagedHookHandle(Hook hook) : IInstalledRuntimeDetour {
	private Hook? hook = hook;
	public void Dispose() => Interlocked.Exchange(ref hook, null)?.Dispose();
	public void DropStrongReferences() => Dispose();
}
