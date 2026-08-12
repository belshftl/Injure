// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.MethodModification;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.MethodModification;

internal sealed class ModDetourDeclarations<L>(
	ILoadedCodeMod mod,
	MethodTargetResolver resolver,
	MethodModificationPhase phase,
	ReloadGeneration generation
) : IStrongRefDroppable, IModDetourDeclarations<L> where L : struct, IModLifetimeIdentity {
	private ILoadedCodeMod? mod = mod;
	private MethodTargetResolver? resolver = resolver;
	private readonly MethodModificationPhase phase = phase;
	private readonly ReloadGeneration generation = generation;

	public void Declare(string targetId, MethodInfo detourMethod, in ModDetourConfig config) {
		ILoadedCodeMod currMod = requireMod();
		MethodTargetDefinition target = requireResolver().Resolve(targetId);
		DetourMethodValidator.ValidateGeneratedDetourMethod(detourMethod, target);
		RuntimeModificationOrder order = MethodModificationDeclarationFactory.CreateOrder(in config);
		getSet(currMod).Add(
			new RuntimeDetourDeclaration {
				OwnerId = currMod.Staged.Manifest.OwnerId,
				Generation = generation,
				Phase = phase,
				Target = new RuntimeMethodTargetKey(target.Method),
				TargetId = target.TargetId,
				Order = order,
				DetourMethod = detourMethod,
			}
		);
	}

	public void Declare(MethodBase targetMethod, MethodInfo detourMethod, in ModDetourConfig config) {
		ILoadedCodeMod currentMod = requireMod();
		DetourMethodValidator.ValidateDirectDetourMethod(detourMethod, targetMethod);
		RuntimeModificationOrder order = MethodModificationDeclarationFactory.CreateOrder(in config);
		getSet(currentMod).Add(
			new RuntimeDetourDeclaration {
				OwnerId = currentMod.Staged.Manifest.OwnerId,
				Generation = generation,
				Phase = phase,
				Target = new RuntimeMethodTargetKey(targetMethod),
				TargetId = null,
				Order = order,
				DetourMethod = detourMethod,
			}
		);
	}

	public void DropStrongReferences() {
		mod = null;
		resolver = null;
	}

	private ILoadedCodeMod requireMod() => mod ?? throw new ModLifecycleContextExpiredException(nameof(IModDetourDeclarations<>), generation);
	private MethodTargetResolver requireResolver() => resolver ?? throw new ModLifecycleContextExpiredException(nameof(IModDetourDeclarations<>), generation);
	private RuntimeDetourDeclarationSet getSet(ILoadedCodeMod currentMod) => phase switch {
		MethodModificationPhase.Load => currentMod.LoadDetours,
		MethodModificationPhase.Link => currentMod.LinkDetours,
		_ => throw new InternalStateException("out of range MethodModificationPhase enum value"),
	};
}
