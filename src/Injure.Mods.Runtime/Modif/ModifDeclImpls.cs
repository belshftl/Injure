// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions;
using Injure.Mods.Abstractions.Modif;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Detours;
using System.Reflection;

namespace Injure.Mods.Runtime.Modif;

/// <summary>
/// Thrown if a mod's detour declaration targets an open generic; detouring open generics is not
/// supported <i>yet</i>, but support is planned before the first stable release, and this exception
/// will be subsequently removed.
/// </summary>
public sealed class ModifOpenGenericsNotImplementedException : NotImplementedException {
	internal ModifOpenGenericsNotImplementedException(string message) : base(message) {
	}
}

internal sealed class DetourDeclImpl<L> : IDetourDecl<L> where L : struct, IModLifetimeIdentity {
	private readonly DetourDeclSet set;

	public DetourDeclImpl(DetourDeclSet set) {
		InternalStateException.ThrowIfNull(set);
		this.set = set;
	}

	public void Declare(Type generatedTargetType, MethodInfo impl, in DetourConfig config) {
		ArgumentNullException.ThrowIfNull(generatedTargetType);
		declare(ModifTargetResolver.ResolveGeneratedTarget(generatedTargetType), impl, config);
	}

	public void Declare(MethodBase targetMethod, MethodInfo impl, in DetourConfig config) {
		ArgumentNullException.ThrowIfNull(targetMethod);
		declare(targetMethod, impl, config);
	}

	private void declare(MethodBase target, MethodInfo impl, in DetourConfig config) {
		ArgumentNullException.ThrowIfNull(impl);
		ModMetadataValidation.ValidateLocalIdOrThrow(config.LocalId);

		// DeclaringType.IsGenericTypeDefinition on Outer<>.Inner.Method is still true, not false
		// verified empirically
		if (target.IsGenericMethodDefinition || (target.DeclaringType?.IsGenericTypeDefinition ?? false))
			throw new ModifOpenGenericsNotImplementedException(
				$"'{target}' is an open generic method; detouring open generics is not supported YET (patching them is supported, though)"
			);

		DetourRegistration d = new(set.OwnerId, config.LocalId, impl);
		set.Add(new DetourDeclDesc(target, config.LocalOrder, config.Before, config.After, d));
	}
}

internal sealed class PatchDeclImpl<L> : IPatchDecl<L> where L : struct, IModLifetimeIdentity {
	private readonly PatchDeclSet set;

	public PatchDeclImpl(PatchDeclSet set) {
		InternalStateException.ThrowIfNull(set);
		this.set = set;
	}

	public void Declare(Type generatedTargetType, IlManipulator<L> manipulator, in PatchConfig config) {
		ArgumentNullException.ThrowIfNull(generatedTargetType);
		declare(ModifTargetResolver.ResolveGeneratedTarget(generatedTargetType), manipulator, config);
	}

	public void Declare(MethodBase targetMethod, IlManipulator<L> manipulator, in PatchConfig config) {
		ArgumentNullException.ThrowIfNull(targetMethod);
		declare(targetMethod, manipulator, config);
	}

	private void declare(MethodBase target, IlManipulator<L> manipulator, in PatchConfig config) {
		ArgumentNullException.ThrowIfNull(manipulator);
		ModMetadataValidation.ValidateLocalIdOrThrow(config.LocalId);
		var p = IlManipulatorRegistration.Create(set.OwnerId, config.LocalId, manipulator);
		set.Add(new PatchDeclDesc(target, config.LocalOrder, config.Before, config.After, p));
	}
}
