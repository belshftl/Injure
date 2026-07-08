// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using MonoMod.RuntimeDetour;
using Injure.Mods.Abstractions.Hooks;

namespace Injure.Mods.Runtime.MonoMod;

internal sealed class MonoModRuntimeHookBackend : IRuntimeHookBackend {
	public IInstalledRuntimeHook InstallManagedHook(in ManagedHookInstallRequest request) {
		InternalStateException.ThrowIfNull(request.TargetMethod);
		InternalStateException.ThrowIfNull(request.HookMethod);
		InternalStateException.ThrowIfInvalidOwnerId(request.OwnerId);
		InternalStateException.ThrowIfInvalidLocalId(request.LocalId);
		Hook? hook = null;
		try {
			hook = new(request.TargetMethod, request.HookMethod);
			return new MonoModManagedHookHandle(hook);
		} catch {
			hook?.Dispose();
			throw;
		}
	}

	public IInstalledRuntimeHook InstallIlHookPipeline(in IlHookPipelineInstallRequest request) {
		InternalStateException.ThrowIfNull(request.TargetMethod);
		InternalStateException.ThrowIfNull(request.GetSnapshot);
		MonoModIlHookState state = new(request.BaselineOwnerId, request.GetSnapshot);
		ILHook? hook = null;
		try {
			hook = new ILHook(request.TargetMethod, state.Apply);
			return new MonoModIlHookHandle(state, hook);
		} catch {
			hook?.Dispose();
			state.Dispose();
			state.DropStrongReferences();
			throw;
		}
	}

	public void DropStrongReferences() {
	}
}
