// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Mods.Abstractions.Modif;
using Injure.Mods.Abstractions.Modif.Il;
using Injure.Mods.Runtime.Modif.Detours;
using System.Reflection;

namespace Injure.Mods.Runtime.Modif;

/// <summary>
/// Scans a mod assembly for <see cref="DetourAttribute"/>/<see cref="PatchAttribute"/> and applies
/// them accordingly.
/// </summary>
/// <remarks>
/// <para>
/// Writes directly into <see cref="DetourDeclSet"/>/<see cref="PatchDeclSet"/> as to avoid the
/// issue of having to get an <c>L</c> somewhere for <see cref="DetourDeclImpl{L}"/> or
/// <see cref="PatchDeclImpl{L}"/>.
/// </para>
/// <para>
/// Currently only has the load phase, since link-phase modifing isn't supported yet.
/// </para>
/// </remarks>
internal static class ModifDiscoverer {
	/// <exception cref="ModifDeclarationException">
	/// Thrown for the first malformed declaration found.
	/// </exception>
	public static void DiscoverLoad(
		Assembly modAssembly,
		Type modLifetimeIdentity,
		DetourDeclSet detours,
		PatchDeclSet patches
	) {
		InternalStateException.ThrowIfNull(modAssembly);
		InternalStateException.ThrowIfNull(modLifetimeIdentity);
		InternalStateException.ThrowIfNull(detours);
		InternalStateException.ThrowIfNull(patches);

		// GetTypes returns nested types too so extra recursion isn't needed
		foreach (Type type in modAssembly.GetTypes()) {
			foreach (
				MethodInfo method in type.GetMethods(
					BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
				)
			) {
				foreach (DetourAttribute attr in method.GetCustomAttributes<DetourAttribute>(inherit: false))
					declareDetour(method, attr, detours);
				foreach (PatchAttribute attribute in method.GetCustomAttributes<PatchAttribute>(inherit: false))
					declarePatch(modLifetimeIdentity, method, attribute, patches);
			}
		}
	}

	private static void declareDetour(
		MethodInfo impl,
		DetourAttribute attr,
		DetourDeclSet set
	) {
		requireNonGenericDeclaringType(impl);
		MethodBase target = resolveTarget(attr);
		if (target.IsGenericMethodDefinition || (target.DeclaringType?.IsGenericTypeDefinition ?? false))
			throw new ModifOpenGenericsNotImplementedException(
				$"'{target}' is an open generic method; detouring open generics is not supported YET (patching them is supported, though)"
			);

		string localId = attr.LocalIdOverride ?? deriveLocalId(impl);
		DetourRegistration d = new(set.OwnerId, localId, impl);
		set.Add(new DetourDeclDesc(
			target,
			attr.LocalOrder,
			buildConstraints(attr.SoftBefore, attr.HardBefore),
			buildConstraints(attr.SoftAfter, attr.HardAfter),
			d
		));
	}

	private static void declarePatch(
		Type modLifetimeIdentity,
		MethodInfo manipulator,
		PatchAttribute attr,
		PatchDeclSet set
	) {
		requireNonGenericDeclaringType(manipulator);
		MethodBase target = resolveTarget(attr);

		Delegate manipDelegate;
		try {
			manipDelegate = Delegate.CreateDelegate(typeof(IlManipulator<>).MakeGenericType(modLifetimeIdentity), manipulator);
		} catch (ArgumentException ex) {
			throw new ModifDeclarationException(
				$"manipulator method '{manipulator}' must be non-generic, return void, and have exactly one parameter of type IlCtx<{modLifetimeIdentity}> (given the mod it's in)",
				ex
			);
		}

		string localId = attr.LocalIdOverride ?? deriveLocalId(manipulator);
		IlManipulatorRegistration m = createManipulatorRegistration(modLifetimeIdentity, set.OwnerId, localId, manipDelegate);
		set.Add(new PatchDeclDesc(
			target,
			attr.LocalOrder,
			buildConstraints(attr.SoftBefore, attr.HardBefore),
			buildConstraints(attr.SoftAfter, attr.HardAfter),
			m
		));
	}

	private static void requireNonGenericDeclaringType(MethodInfo method) {
		// DeclaringType.IsGenericTypeDefinition on Outer<>.Inner.Method is still true, not false
		// verified empirically
		if (method.DeclaringType!.IsGenericTypeDefinition)
			throw new ModifDeclarationException(
				$"'{method}' declares a modif attribute, but its declaring type '{method.DeclaringType}' is generic or nested within a generic type; the declaring type must be fully non-generic"
			);
	}

	private static MethodBase resolveTarget(Attribute attr) => attr switch {
		LoadDetourAttribute a => ModifTargetResolver.ResolveGeneratedTarget(a.GeneratedTargetType),
		LoadPatchAttribute a => ModifTargetResolver.ResolveGeneratedTarget(a.GeneratedTargetType),
		LoadMethodDetourAttribute a => ModifTargetResolver.ResolveNamedTarget(a.DeclaringType, a.MethodName, a.BindingFlags, a.ParameterTypes),
		LoadMethodPatchAttribute a => ModifTargetResolver.ResolveNamedTarget(a.DeclaringType, a.MethodName, a.BindingFlags, a.ParameterTypes),
		_ => throw new InternalStateException($"unknown DetourAttribute/PatchAttribute derived type '{attr.GetType()}'"),
	};

	private static string deriveLocalId(MethodInfo method) => $"{method.DeclaringType!.FullName}.{method.Name}";

	private static IlManipulatorRegistration createManipulatorRegistration(
		Type modLifetimeIdentity,
		string ownerId,
		string localId,
		Delegate manipulator
	) {
		MethodInfo factory = typeof(IlManipulatorRegistration)
			.GetMethod(nameof(IlManipulatorRegistration.Create), BindingFlags.Static | BindingFlags.Public)!
			.MakeGenericMethod(modLifetimeIdentity);
		return (IlManipulatorRegistration)factory.Invoke(null, [ownerId, localId, manipulator])!;
	}

	private static List<OwnerOrderingConstraint>? buildConstraints(string[]? softOwnerIds, string[]? hardOwnerIds) {
		if ((softOwnerIds?.Length ?? 0) == 0 && (hardOwnerIds?.Length ?? 0) == 0)
			return null;

		List<OwnerOrderingConstraint> constraints = new();
		if (softOwnerIds is not null)
			foreach (string ownerId in softOwnerIds)
				constraints.Add(OwnerOrderingConstraint.SoftOwner(ownerId));
		if (hardOwnerIds is not null)
			foreach (string ownerId in hardOwnerIds)
				constraints.Add(OwnerOrderingConstraint.HardOwner(ownerId));
		return constraints;
	}
}
