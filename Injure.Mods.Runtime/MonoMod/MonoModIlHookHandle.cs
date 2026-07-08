// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using MonoMod.RuntimeDetour;
using Injure.Mods.Abstractions.Hooks;

namespace Injure.Mods.Runtime.MonoMod;

internal sealed class MonoModIlHookHandle(MonoModIlHookState state, ILHook hook) : IInstalledRuntimeHook {
	private MonoModIlHookState? state = state;
	private ILHook? hook = hook;

	public void Dispose() {
		Interlocked.Exchange(ref hook, null)?.Dispose();
		MonoModIlHookState? s = Interlocked.Exchange(ref state, null);
		s?.Dispose();
		s?.DropStrongReferences();
	}

	public void DropStrongReferences() => Dispose();
}
