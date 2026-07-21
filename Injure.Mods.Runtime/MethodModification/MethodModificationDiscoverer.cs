// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions.MethodModification;
using Injure.Mods.Abstractions.MethodModification.Il;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.MethodModification;

internal static class MethodModificationDiscoverer {
	public static void DiscoverLoadMethodModifications(ILoadedCodeMod mod, MethodTargetResolver resolver) {
		InternalStateException.ThrowIfNull(mod);
		InternalStateException.ThrowIfNull(resolver);
		string ownerId = mod.Staged.Manifest.OwnerId;
		Type lifetimeIdentityType = mod.LifetimeIdentityType;
		foreach (Type type in getTypesStrict(mod.Assembly, ownerId)) {
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) {
				discoverLoadDetourAttributes(mod, method, resolver, ownerId);
				discoverLoadPatchAttributes(mod, method, resolver, ownerId, lifetimeIdentityType);
				discoverLoadMethodDetourAttributes(mod, method, ownerId);
				discoverLoadMethodPatchAttributes(mod, method, ownerId, lifetimeIdentityType);
			}
		}
	}

	private static void discoverLoadDetourAttributes(ILoadedCodeMod mod, MethodInfo detourMethod, MethodTargetResolver resolver, string ownerId) {
		int n = 0;
		foreach (LoadDetourAttribute attr in detourMethod.GetCustomAttributes<LoadDetourAttribute>()) {
			MethodTargetDefinition target = resolver.Resolve(attr.TargetId);
			DetourMethodValidator.ValidateGeneratedDetourMethod(detourMethod, target);
			RuntimeModificationOrder order = MethodModificationDeclarationFactory.CreateOrder(attr, detourMethod, n++, "attr-load-detour");
			mod.LoadDetours.Add(
				new RuntimeDetourDeclaration {
					OwnerId = ownerId,
					Generation = mod.Staged.Generation,
					Phase = MethodModificationPhase.Load,
					Target = new RuntimeMethodTargetKey(target.Method),
					TargetId = target.TargetId,
					Order = order,
					DetourMethod = detourMethod,
				}
			);
		}
	}

	private static void discoverLoadPatchAttributes(ILoadedCodeMod mod, MethodInfo manipulatorMethod, MethodTargetResolver resolver, string ownerId, Type lifetimeIdentityType) {
		int n = 0;
		foreach (LoadPatchAttribute attr in manipulatorMethod.GetCustomAttributes<LoadPatchAttribute>()) {
			MethodTargetDefinition target = resolver.Resolve(attr.TargetId);
			PatchMethodValidator.ValidateGeneratedPatchMethod(manipulatorMethod, target, lifetimeIdentityType);
			RuntimeModificationOrder order = MethodModificationDeclarationFactory.CreateOrder(attr, manipulatorMethod, n++, "attr-load-patch");
			IlManipulatorRegistration registration =
				MethodModificationDeclarationFactory.CreatePatchRegistrationFromMethod(
					lifetimeIdentityType,
					ownerId,
					order.LocalId,
					manipulatorMethod
				);
			mod.LoadPatches.Add(
				new RuntimePatchDeclaration {
					OwnerId = ownerId,
					Generation = mod.Staged.Generation,
					Phase = MethodModificationPhase.Load,
					Target = new RuntimeMethodTargetKey(target.Method),
					TargetId = target.TargetId,
					Order = order,
					Registration = registration,
				}
			);
		}
	}

	private static void discoverLoadMethodDetourAttributes(ILoadedCodeMod mod, MethodInfo detourMethod, string ownerId) {
		int n = 0;
		foreach (LoadMethodDetourAttribute attr in detourMethod.GetCustomAttributes<LoadMethodDetourAttribute>()) {
			MethodBase target = resolveMethod(attr.TargetType, attr.MethodName, attr.BindingFlags, attr.ParameterTypes);
			DetourMethodValidator.ValidateDirectDetourMethod(detourMethod, target);
			RuntimeModificationOrder order = MethodModificationDeclarationFactory.CreateOrder(attr, detourMethod, n++, "attr-load-method-detour");
			mod.LoadDetours.Add(
				new RuntimeDetourDeclaration {
					OwnerId = ownerId,
					Generation = mod.Staged.Generation,
					Phase = MethodModificationPhase.Load,
					Target = new RuntimeMethodTargetKey(target),
					TargetId = null,
					Order = order,
					DetourMethod = detourMethod,
				}
			);
		}
	}

	private static void discoverLoadMethodPatchAttributes(ILoadedCodeMod mod, MethodInfo manipulatorMethod, string ownerId, Type lifetimeIdentityType) {
		int n = 0;
		foreach (LoadMethodPatchAttribute attr in manipulatorMethod.GetCustomAttributes<LoadMethodPatchAttribute>()) {
			MethodBase target = resolveMethod(attr.TargetType, attr.MethodName, attr.BindingFlags, attr.ParameterTypes);
			PatchMethodValidator.ValidateDirectPatchMethod(manipulatorMethod, target, lifetimeIdentityType);
			RuntimeModificationOrder order = MethodModificationDeclarationFactory.CreateOrder(attr, manipulatorMethod, n++, "attr-load-method-patch");
			IlManipulatorRegistration registration =
				MethodModificationDeclarationFactory.CreatePatchRegistrationFromMethod(
					lifetimeIdentityType,
					ownerId,
					order.LocalId,
					manipulatorMethod
				);
			mod.LoadPatches.Add(
				new RuntimePatchDeclaration {
					OwnerId = ownerId,
					Generation = mod.Staged.Generation,
					Phase = MethodModificationPhase.Load,
					Target = new RuntimeMethodTargetKey(target),
					TargetId = null,
					Order = order,
					Registration = registration,
				}
			);
		}
	}

	private static MethodInfo resolveMethod(Type type, string name, BindingFlags flags, Type[]? parameterTypes) {
		if (parameterTypes is not null) {
			MethodInfo? method = type.GetMethod(name, flags, binder: null, types: parameterTypes, modifiers: null);
			return method ?? throw new MissingMethodException(type.FullName, name);
		}
		MethodInfo[] matches = type.GetMethods(flags).Where(method => method.Name == name).ToArray();
		return matches.Length switch {
			1 => matches[0],
			0 => throw new MissingMethodException(type.FullName, name),
			_ => throw new AmbiguousMatchException($"method '{type.FullName}.{name}' is overloaded; specify ParameterTypes"),
		};
	}

	private static Type[] getTypesStrict(Assembly assembly, string ownerId) {
		try {
			return assembly.GetTypes();
		} catch (ReflectionTypeLoadException ex) {
			string details = string.Join(
				Environment.NewLine,
				ex.LoaderExceptions.Where(exception => exception is not null).Select(exception => "  - " + exception!.Message)
			);
			throw new MethodModificationValidationException($"couldn't inspect all method modification types for mod '{ownerId}':\n{details}");
		}
	}
}
