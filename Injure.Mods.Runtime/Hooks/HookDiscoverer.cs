// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;
using Injure.Mods.Abstractions.Hooks;
using Injure.Mods.Abstractions.Hooks.Il;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.Hooks;

internal static class HookDiscoverer {
	public static void DiscoverLoadHooks(ILoadedCodeMod mod, HookTargetResolver resolver) {
		InternalStateException.ThrowIfNull(mod);
		InternalStateException.ThrowIfNull(resolver);
		string ownerId = mod.Staged.Manifest.OwnerId;
		Type lifetimeIdentityType = mod.LifetimeIdentityType;
		foreach (Type type in getTypesStrict(mod.Assembly, ownerId)) {
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) {
				discoverLoadHookAttributes(mod, method, resolver, ownerId);
				discoverLoadIlHookAttributes(mod, method, resolver, ownerId, lifetimeIdentityType);
				discoverLoadMethodHookAttributes(mod, method, ownerId);
				discoverLoadMethodIlHookAttributes(mod, method, ownerId, lifetimeIdentityType);
			}
		}
	}

	private static void discoverLoadHookAttributes(
		ILoadedCodeMod mod,
		MethodInfo hookMethod,
		HookTargetResolver resolver,
		string ownerId
	) {
		int n = 0;
		foreach (LoadHookAttribute attr in hookMethod.GetCustomAttributes<LoadHookAttribute>()) {
			HookTargetDefinition target = resolver.Resolve(attr.TargetId);
			HookMethodValidator.ValidateGeneratedManagedHookMethod(hookMethod, target);
			RuntimeHookOrder order = HookDeclarationFactory.CreateOrder(attr, hookMethod, n++, "attr-load-hook");
			mod.LoadHooks.Add(
				new ManagedHookDeclaration {
					OwnerId = ownerId,
					Generation = mod.Staged.Generation,
					Phase = HookDeclarationPhase.Load,
					Kind = RuntimeHookKind.Managed,
					Target = new RuntimeHookTargetKey(target.Method, target.TargetId),
					Order = order,
					HookMethod = hookMethod,
				}
			);
		}
	}

	private static void discoverLoadIlHookAttributes(
		ILoadedCodeMod mod,
		MethodInfo manipulatorMethod,
		HookTargetResolver resolver,
		string ownerId,
		Type lifetimeIdentityType
	) {
		int n = 0;
		foreach (LoadIlHookAttribute attr in manipulatorMethod.GetCustomAttributes<LoadIlHookAttribute>()) {
			HookTargetDefinition target = resolver.Resolve(attr.TargetId);
			HookMethodValidator.ValidateGeneratedIlHookMethod(manipulatorMethod, target, lifetimeIdentityType);
			RuntimeHookOrder order = HookDeclarationFactory.CreateOrder(attr, manipulatorMethod, n++, "attr-load-il-hook");
			IlManipulatorRegistration registration = HookDeclarationFactory.CreateRegistrationFromMethod(
				lifetimeIdentityType,
				ownerId,
				order.LocalId,
				manipulatorMethod
			);
			mod.LoadHooks.Add(
				new IlHookDeclaration {
					OwnerId = ownerId,
					Generation = mod.Staged.Generation,
					Phase = HookDeclarationPhase.Load,
					Kind = RuntimeHookKind.Il,
					Target = new RuntimeHookTargetKey(target.Method, target.TargetId),
					Order = order,
					Registration = registration,
				}
			);
		}
	}

	private static void discoverLoadMethodHookAttributes(
		ILoadedCodeMod mod,
		MethodInfo hookMethod,
		string ownerId
	) {
		int n = 0;
		foreach (LoadMethodHookAttribute attr in hookMethod.GetCustomAttributes<LoadMethodHookAttribute>()) {
			MethodBase target = resolveMethod(attr.TargetType, attr.MethodName, attr.BindingFlags, attr.ParameterTypes);
			HookMethodValidator.ValidateDirectManagedHookMethod(hookMethod, target);
			RuntimeHookOrder order = HookDeclarationFactory.CreateOrder(attr, hookMethod, n++, "attr-load-method-hook");
			mod.LoadHooks.Add(
				new ManagedHookDeclaration {
					OwnerId = ownerId,
					Generation = mod.Staged.Generation,
					Phase = HookDeclarationPhase.Load,
					Kind = RuntimeHookKind.Managed,
					Target = new RuntimeHookTargetKey(target, TargetId: null),
					Order = order,
					HookMethod = hookMethod,
				}
			);
		}
	}

	private static void discoverLoadMethodIlHookAttributes(
		ILoadedCodeMod mod,
		MethodInfo manipulatorMethod,
		string ownerId,
		Type lifetimeIdentityType
	) {
		int n = 0;
		foreach (LoadMethodIlHookAttribute attr in manipulatorMethod.GetCustomAttributes<LoadMethodIlHookAttribute>()) {
			MethodBase target = resolveMethod(attr.TargetType, attr.MethodName, attr.BindingFlags, attr.ParameterTypes);
			HookMethodValidator.ValidateDirectIlHookMethod(manipulatorMethod, target, lifetimeIdentityType);
			RuntimeHookOrder order = HookDeclarationFactory.CreateOrder(attr, manipulatorMethod, n++, "attr-load-method-il-hook");
			IlManipulatorRegistration registration = HookDeclarationFactory.CreateRegistrationFromMethod(
				lifetimeIdentityType,
				ownerId,
				order.LocalId,
				manipulatorMethod
			);
			mod.LoadHooks.Add(
				new IlHookDeclaration {
					OwnerId = ownerId,
					Generation = mod.Staged.Generation,
					Phase = HookDeclarationPhase.Load,
					Kind = RuntimeHookKind.Il,
					Target = new RuntimeHookTargetKey(target, TargetId: null),
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
		MethodInfo[] matches = type.GetMethods(flags).Where(m => m.Name == name).ToArray();
		if (matches.Length == 1)
			return matches[0];
		if (matches.Length == 0)
			throw new MissingMethodException(type.FullName, name);
		throw new AmbiguousMatchException($"method '{type.FullName}.{name}' is overloaded; specify ParameterTypes");
	}

	private static Type[] getTypesStrict(Assembly asm, string ownerId) {
		try {
			return asm.GetTypes();
		} catch (ReflectionTypeLoadException ex) {
			string details = string.Join(Environment.NewLine, ex.LoaderExceptions.Where(e => e is not null).Select(e => "  - " + e!.Message));
			throw new HookValidationException($"couldn't inspect all hook types for mod '{ownerId}':\n{details}");
		}
	}
}
