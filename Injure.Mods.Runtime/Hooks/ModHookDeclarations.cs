// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Hooks;
using Injure.Mods.Abstractions.Hooks.Il;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.Hooks;

internal sealed class ModHookDeclarations<L>(
	ILoadedCodeMod mod,
	HookTargetResolver resolver,
	HookDeclarationPhase phase,
	ReloadGeneration generation
) : IStrongRefDroppable, IModHookDeclarations<L> where L : struct, IModLifetimeIdentity {
	private ILoadedCodeMod? mod = mod;
	private HookTargetResolver? resolver = resolver;
	private readonly HookDeclarationPhase phase = phase;
	private readonly ReloadGeneration generation = generation;

	public void DeclareHook(string targetId, MethodInfo hookMethod, in ModHookConfig config) {
		ILoadedCodeMod currentMod = requireMod();
		HookTargetResolver currentResolver = requireResolver();
		HookTargetDefinition target = currentResolver.Resolve(targetId);
		HookMethodValidator.ValidateGeneratedManagedHookMethod(hookMethod, target);
		RuntimeHookOrder order = HookDeclarationFactory.CreateOrder(in config);

		getSet(currentMod).Add(
			new ManagedHookDeclaration {
				OwnerId = currentMod.Staged.Manifest.OwnerId,
				Generation = generation,
				Phase = phase,
				Kind = RuntimeHookKind.Managed,
				Target = new RuntimeHookTargetKey(target.Method, target.TargetId),
				Order = order,
				HookMethod = hookMethod,
			}
		);
	}

	public void DeclareHook(MethodBase targetMethod, MethodInfo hookMethod, in ModHookConfig config) {
		ILoadedCodeMod currentMod = requireMod();
		HookMethodValidator.ValidateDirectManagedHookMethod(hookMethod, targetMethod);
		RuntimeHookOrder order = HookDeclarationFactory.CreateOrder(in config);

		getSet(currentMod).Add(
			new ManagedHookDeclaration {
				OwnerId = currentMod.Staged.Manifest.OwnerId,
				Generation = generation,
				Phase = phase,
				Kind = RuntimeHookKind.Managed,
				Target = new RuntimeHookTargetKey(targetMethod, TargetId: null),
				Order = order,
				HookMethod = hookMethod,
			}
		);
	}

	public void DeclareIlHook(string targetId, IlManipulator<L> manipulator, in ModHookConfig config) {
		ILoadedCodeMod currentMod = requireMod();
		HookTargetResolver currentResolver = requireResolver();
		HookTargetDefinition target = currentResolver.Resolve(targetId);
		RuntimeHookOrder order = HookDeclarationFactory.CreateOrder(in config);
		var registration = IlManipulatorRegistration.Create(currentMod.Staged.Manifest.OwnerId, order.LocalId, manipulator);

		getSet(currentMod).Add(
			new IlHookDeclaration {
				OwnerId = currentMod.Staged.Manifest.OwnerId,
				Generation = generation,
				Phase = phase,
				Kind = RuntimeHookKind.Il,
				Target = new RuntimeHookTargetKey(target.Method, target.TargetId),
				Order = order,
				Registration = registration,
			}
		);
	}

	public void DeclareIlHook(MethodBase targetMethod, IlManipulator<L> manipulator, in ModHookConfig config) {
		ILoadedCodeMod currentMod = requireMod();
		RuntimeHookOrder order = HookDeclarationFactory.CreateOrder(in config);
		var registration = IlManipulatorRegistration.Create(currentMod.Staged.Manifest.OwnerId, order.LocalId, manipulator);

		getSet(currentMod).Add(
			new IlHookDeclaration {
				OwnerId = currentMod.Staged.Manifest.OwnerId,
				Generation = generation,
				Phase = phase,
				Kind = RuntimeHookKind.Il,
				Target = new RuntimeHookTargetKey(targetMethod, TargetId: null),
				Order = order,
				Registration = registration,
			}
		);
	}

	public void DropStrongReferences() {
		mod = null;
		resolver = null;
	}

	private ILoadedCodeMod requireMod() => mod ?? throw new ModLifecycleContextExpiredException(nameof(IModHookDeclarations<>), generation);
	private HookTargetResolver requireResolver() => resolver ?? throw new ModLifecycleContextExpiredException(nameof(IModHookDeclarations<>), generation);
	private RuntimeHookDeclarationSet getSet(ILoadedCodeMod currentMod) => phase switch {
		HookDeclarationPhase.Load => currentMod.LoadHooks,
		HookDeclarationPhase.Link => currentMod.LinkHooks,
		_ => throw new InternalStateException("out of range HookDeclarationPhase enum value"),
	};
}
