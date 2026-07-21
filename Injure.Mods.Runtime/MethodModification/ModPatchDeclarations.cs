// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.MethodModification;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.MethodModification;

internal sealed class ModPatchDeclarations<L>(
	ILoadedCodeMod mod,
	MethodTargetResolver resolver,
	MethodModificationPhase phase,
	ReloadGeneration generation
) : IStrongRefDroppable, IModPatchDeclarations<L> where L : struct, IModLifetimeIdentity {
	private ILoadedCodeMod? mod = mod;
	private MethodTargetResolver? resolver = resolver;
	private readonly MethodModificationPhase phase = phase;
	private readonly ReloadGeneration generation = generation;

	public void Declare(string targetId, IlManipulator<L> manipulator, in ModPatchConfig config) {
		ILoadedCodeMod currentMod = requireMod();
		MethodTargetDefinition target = requireResolver().Resolve(targetId);
		PatchMethodValidator.ValidateTarget(target.Method, target.TargetId);
		RuntimeModificationOrder order = MethodModificationDeclarationFactory.CreateOrder(in config);
		var registration = IlManipulatorRegistration.Create(currentMod.Staged.Manifest.OwnerId, order.LocalId, manipulator);
		getSet(currentMod).Add(
			new RuntimePatchDeclaration {
				OwnerId = currentMod.Staged.Manifest.OwnerId,
				Generation = generation,
				Phase = phase,
				Target = new RuntimeMethodTargetKey(target.Method),
				TargetId = target.TargetId,
				Order = order,
				Registration = registration,
			}
		);
	}

	public void Declare(MethodBase targetMethod, IlManipulator<L> manipulator, in ModPatchConfig config) {
		ILoadedCodeMod currentMod = requireMod();
		PatchMethodValidator.ValidateTarget(targetMethod, MethodDisplay.FormatMethod(targetMethod));
		RuntimeModificationOrder order = MethodModificationDeclarationFactory.CreateOrder(in config);
		var registration = IlManipulatorRegistration.Create(currentMod.Staged.Manifest.OwnerId, order.LocalId, manipulator);
		getSet(currentMod).Add(
			new RuntimePatchDeclaration {
				OwnerId = currentMod.Staged.Manifest.OwnerId,
				Generation = generation,
				Phase = phase,
				Target = new RuntimeMethodTargetKey(targetMethod),
				TargetId = null,
				Order = order,
				Registration = registration,
			}
		);
	}

	public void DropStrongReferences() {
		mod = null;
		resolver = null;
	}

	private ILoadedCodeMod requireMod() => mod ?? throw new ModLifecycleContextExpiredException(nameof(IModPatchDeclarations<>), generation);
	private MethodTargetResolver requireResolver() => resolver ?? throw new ModLifecycleContextExpiredException(nameof(IModPatchDeclarations<>), generation);
	private RuntimePatchDeclarationSet getSet(ILoadedCodeMod currentMod) => phase switch {
		MethodModificationPhase.Load => currentMod.LoadPatches,
		MethodModificationPhase.Link => currentMod.LinkPatches,
		_ => throw new InternalStateException("out of range MethodModificationPhase enum value"),
	};
}
