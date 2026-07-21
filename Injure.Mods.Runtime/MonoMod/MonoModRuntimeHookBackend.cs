// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using MonoMod.RuntimeDetour;
using Injure.Mods.Abstractions.MethodModification;

namespace Injure.Mods.Runtime.MonoMod;

internal sealed class MonoModRuntimeHookBackend : IRuntimeDetourBackend, IRuntimePatchBackend {
	IInstalledRuntimeDetour IRuntimeDetourBackend.Install(in DetourInstallRequest request) {
		InternalStateException.ThrowIfNull(request.TargetMethod);
		InternalStateException.ThrowIfNull(request.DetourMethod);
		InternalStateException.ThrowIfInvalidOwnerId(request.OwnerId);
		InternalStateException.ThrowIfInvalidLocalId(request.LocalId);
		Hook? hook = null;
		try {
			hook = new Hook(request.TargetMethod, request.DetourMethod);
			return new MonoModManagedHookHandle(hook);
		} catch {
			hook?.Dispose();
			throw;
		}
	}

	IInstalledRuntimePatch IRuntimePatchBackend.InstallPipeline(in PatchPipelineInstallRequest request) {
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
