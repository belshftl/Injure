// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection;
using Injure.Mods.MonoMod;
using MonoMod.RuntimeDetour;

namespace Injure.Mods.Runtime.MonoMod;

internal static class HookDiscoverer<TGameApi> {
	public static void DiscoverLoadHooks(LoadedCodeMod<TGameApi> mod, HookTargetResolver resolver) {
		foreach (Type type in getTypesStrict(mod.Assembly, mod.Staged.Manifest.OwnerId)) {
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)) {
				discoverLoadHookAttributes(mod, method, resolver);
				discoverLoadILHookAttributes(mod, method, resolver);
				discoverLoadMethodHookAttributes(mod, method);
				discoverLoadMethodILHookAttributes(mod, method);
			}
		}
	}

	private static void discoverLoadHookAttributes(LoadedCodeMod<TGameApi> mod, MethodInfo hookMethod, HookTargetResolver resolver) {
		int n = 0;
		foreach (LoadHookAttribute attr in hookMethod.GetCustomAttributes<LoadHookAttribute>()) {
			HookTarget target = resolver.Resolve(attr.TargetID);
			HookMethodValidator.ValidateGeneratedHookMethod(hookMethod, target);
			mod.LoadHooks.Add(
				new HookDeclaration(
					mod.Staged.Manifest.OwnerId,
					CreateOrder(mod.Staged.Manifest.OwnerId, attr, hookMethod, n++, "attr-load-hook"),
					detourConfigFor(mod.Staged.Manifest.OwnerId, hookMethod, attr),
					target.Method,
					hookMethod
				)
			);
		}
	}

	private static void discoverLoadILHookAttributes(LoadedCodeMod<TGameApi> mod, MethodInfo manipulatorMethod, HookTargetResolver resolver) {
		int n = 0;
		foreach (LoadILHookAttribute attr in manipulatorMethod.GetCustomAttributes<LoadILHookAttribute>()) {
			HookTarget target = resolver.Resolve(attr.TargetID);
			HookMethodValidator.ValidateGeneratedILHookMethod(manipulatorMethod, target);
			mod.LoadHooks.Add(
				new ILHookDeclaration(
					mod.Staged.Manifest.OwnerId,
					CreateOrder(mod.Staged.Manifest.OwnerId, attr, manipulatorMethod, n++, "attr-load-il-hook"),
					detourConfigFor(mod.Staged.Manifest.OwnerId, manipulatorMethod, attr),
					target.Method,
					manipulatorMethod
				)
			);
		}
	}

	private static void discoverLoadMethodHookAttributes(LoadedCodeMod<TGameApi> mod, MethodInfo hookMethod) {
		int n = 0;
		foreach (LoadMethodHookAttribute attr in hookMethod.GetCustomAttributes<LoadMethodHookAttribute>()) {
			MethodBase target = resolveMethod(attr.TargetType, attr.MethodName, attr.BindingFlags, attr.ParameterTypes);
			HookMethodValidator.ValidateDirectHookMethod(hookMethod, target);
			mod.LoadHooks.Add(
				new HookDeclaration(
					mod.Staged.Manifest.OwnerId,
					CreateOrder(mod.Staged.Manifest.OwnerId, attr, hookMethod, n++, "attr-load-method-hook"),
					detourConfigFor(mod.Staged.Manifest.OwnerId, hookMethod, attr),
					target,
					hookMethod
				)
			);
		}
	}

	private static void discoverLoadMethodILHookAttributes(LoadedCodeMod<TGameApi> mod, MethodInfo manipulatorMethod) {
		int n = 0;
		foreach (LoadMethodILHookAttribute attr in manipulatorMethod.GetCustomAttributes<LoadMethodILHookAttribute>()) {
			MethodBase target = resolveMethod(attr.TargetType, attr.MethodName, attr.BindingFlags, attr.ParameterTypes);
			HookMethodValidator.ValidateDirectILHookMethod(manipulatorMethod, target);
			mod.LoadHooks.Add(
				new ILHookDeclaration(
					mod.Staged.Manifest.OwnerId,
					CreateOrder(mod.Staged.Manifest.OwnerId, attr, manipulatorMethod, n++, "attr-load-method-il-hook"),
					detourConfigFor(mod.Staged.Manifest.OwnerId, manipulatorMethod, attr),
					target,
					manipulatorMethod
				)
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

	public static HookOrder CreateOrder(string ownerId, string? orderDomain, int localPriority, MethodInfo patchMethod, int ordinal, string prefix) {
		if (patchMethod.DeclaringType?.FullName is null)
			throw new InvalidOperationException("expected patch method to have a declaring type with a fully-qualified name");
		string domain = string.IsNullOrWhiteSpace(orderDomain) ? ownerId : ownerId + "::" + orderDomain;
		string localId = prefix + ":" + patchMethod.DeclaringType.FullName + "." + patchMethod.Name + "#" + ordinal.ToString(CultureInfo.InvariantCulture);
		return new HookOrder(domain, localId, localPriority);
	}

	public static HookOrder CreateOrder(string ownerId, IHookAttribute attr, MethodInfo patchMethod, int ordinal, string prefix) =>
		CreateOrder(ownerId, attr.OrderDomain, attr.LocalPriority, patchMethod, ordinal, prefix);

	private static DetourConfig detourConfigFor(string ownerId, MethodInfo patchMethod, IHookAttribute attr) => new(
		id: attr.DetourIDOverride ?? autoDetourIDFor(ownerId, patchMethod),
		priority: attr.DetourPriority,
		before: attr.DetourBefore,
		after: attr.DetourAfter
	);

	private static string autoDetourIDFor(string ownerId, MethodInfo patchMethod) {
		if (patchMethod.DeclaringType?.FullName is null)
			throw new InvalidOperationException("expected patch method to have a declaring type with a fully-qualified name");
		return $"{ownerId}::{patchMethod.DeclaringType.FullName}.{patchMethod.Name}";
	}

	private static Type[] getTypesStrict(Assembly assembly, string ownerId) {
		try {
			return assembly.GetTypes();
		} catch (ReflectionTypeLoadException ex) {
			string details = string.Join(Environment.NewLine, ex.LoaderExceptions.Where(e => e is not null).Select(e => "  - " + e!.Message));
			throw new InvalidOperationException($"could not inspect all hook types for mod '{ownerId}':\n{details}", ex);
		}
	}
}
