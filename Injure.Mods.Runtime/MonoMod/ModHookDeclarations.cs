// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

using Injure.Mods.MonoMod;

using MonoMod.RuntimeDetour;

namespace Injure.Mods.Runtime.MonoMod;

internal enum HookDeclarationPhase {
	Load,
	//Link,
}

internal sealed class ModHookDeclarations<TGameApi, L>(
	LoadedCodeMod<TGameApi> mod,
	HookTargetResolver resolver,
	HookDeclarationPhase phase,
	ReloadGeneration generation
) : IStrongRefDroppable, IModHookDeclarations<L> where L : struct, IModLifetimeIdentity {
	private LoadedCodeMod<TGameApi>? mod = mod;
	private HookTargetResolver? resolver = resolver;
	private readonly HookDeclarationPhase phase = phase;
	private readonly ReloadGeneration generation = generation;

	public void DeclareHook(string targetID, MethodInfo hookMethod, in ModHookConfig config) {
		if (mod is null || resolver is null)
			throw new ModLifecycleContextExpiredException(nameof(IModHookDeclarations<>), generation);
		HookTarget target = resolver.Resolve(targetID);
		HookMethodValidator.ValidateGeneratedHookMethod(hookMethod, target);
		GenerationPatchSet set = phase switch {
			HookDeclarationPhase.Load => mod.LoadHooks,
			_ => throw new InternalStateException("out of range HookDeclarationPhase enum value"),
		};
		set.Add(
			new HookDeclaration(
				mod.Staged.Manifest.OwnerID,
				HookDiscoverer<TGameApi>.CreateOrder(mod.Staged.Manifest.OwnerID, config.OrderDomain, config.LocalPriority, hookMethod, 0, "decl-load-hook"),
				detourConfigFrom(in config),
				target.Method,
				hookMethod
			)
		);
	}

	public void DeclareHook(MethodBase targetMethod, MethodInfo hookMethod, in ModHookConfig config) {
		if (mod is null || resolver is null)
			throw new ModLifecycleContextExpiredException(nameof(IModHookDeclarations<>), generation);
		HookMethodValidator.ValidateDirectHookMethod(hookMethod, targetMethod);
		GenerationPatchSet set = phase switch {
			HookDeclarationPhase.Load => mod.LoadHooks,
			_ => throw new InternalStateException("out of range HookDeclarationPhase enum value"),
		};
		set.Add(
			new HookDeclaration(
				mod.Staged.Manifest.OwnerID,
				HookDiscoverer<TGameApi>.CreateOrder(mod.Staged.Manifest.OwnerID, config.OrderDomain, config.LocalPriority, hookMethod, 0, "decl-load-hook"),
				detourConfigFrom(in config),
				targetMethod,
				hookMethod
			)
		);
	}

	public void DeclareILHook(string targetID, MethodInfo manipulatorMethod, in ModHookConfig config) {
		if (mod is null || resolver is null)
			throw new ModLifecycleContextExpiredException(nameof(IModHookDeclarations<>), generation);
		HookTarget target = resolver.Resolve(targetID);
		HookMethodValidator.ValidateGeneratedILHookMethod(manipulatorMethod, target);
		GenerationPatchSet set = phase switch {
			HookDeclarationPhase.Load => mod.LoadHooks,
			_ => throw new InternalStateException("out of range HookDeclarationPhase enum value"),
		};
		set.Add(
			new ILHookDeclaration(
				mod.Staged.Manifest.OwnerID,
				HookDiscoverer<TGameApi>.CreateOrder(mod.Staged.Manifest.OwnerID, config.OrderDomain, config.LocalPriority, manipulatorMethod, 0, "decl-load-hook"),
				detourConfigFrom(in config),
				target.Method,
				manipulatorMethod
			)
		);
	}

	public void DeclareILHook(MethodBase targetMethod, MethodInfo manipulatorMethod, in ModHookConfig config) {
		if (mod is null || resolver is null)
			throw new ModLifecycleContextExpiredException(nameof(IModHookDeclarations<>), generation);
		HookMethodValidator.ValidateDirectILHookMethod(manipulatorMethod, targetMethod);
		GenerationPatchSet set = phase switch {
			HookDeclarationPhase.Load => mod.LoadHooks,
			_ => throw new InternalStateException("out of range HookDeclarationPhase enum value"),
		};
		set.Add(
			new ILHookDeclaration(
				mod.Staged.Manifest.OwnerID,
				HookDiscoverer<TGameApi>.CreateOrder(mod.Staged.Manifest.OwnerID, config.OrderDomain, config.LocalPriority, manipulatorMethod, 0, "decl-load-hook"),
				detourConfigFrom(in config),
				targetMethod,
				manipulatorMethod
			)
		);
	}

	public void DropStrongReferences() {
		mod = null;
		resolver = null;
	}

	private static DetourConfig detourConfigFrom(in ModHookConfig config) => new(
		config.DetourID ?? throw new InvalidOperationException("ModHookConfig must specify a non-null detour ID"),
		config.DetourPriority,
		config.DetourBefore,
		config.DetourAfter
	);
}
