// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Reflection;

using MonoMod.RuntimeDetour;

using Injure.ModKit.Abstractions.MonoMod;
using Injure.ModKit.Runtime;

namespace Injure.ModKit.MonoMod;

internal static class HookDiscoverer<TGameApi> {
	public static void DiscoverLoadHooks(LoadedCodeMod<TGameApi> mod, HookTargetResolver resolver) {
		foreach (Type type in getTypesStrict(mod.Assembly, mod.Staged.Manifest.OwnerID)) {
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
					mod.Staged.Manifest.OwnerID,
					CreateOrder(mod.Staged.Manifest.OwnerID, attr, hookMethod, n++, "attr-load-hook"),
					detourConfigFor(mod.Staged.Manifest.OwnerID, hookMethod, attr),
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
					mod.Staged.Manifest.OwnerID,
					CreateOrder(mod.Staged.Manifest.OwnerID, attr, manipulatorMethod, n++, "attr-load-il-hook"),
					detourConfigFor(mod.Staged.Manifest.OwnerID, manipulatorMethod, attr),
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
					mod.Staged.Manifest.OwnerID,
					CreateOrder(mod.Staged.Manifest.OwnerID, attr, hookMethod, n++, "attr-load-method-hook"),
					detourConfigFor(mod.Staged.Manifest.OwnerID, hookMethod, attr),
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
					mod.Staged.Manifest.OwnerID,
					CreateOrder(mod.Staged.Manifest.OwnerID, attr, manipulatorMethod, n++, "attr-load-method-il-hook"),
					detourConfigFor(mod.Staged.Manifest.OwnerID, manipulatorMethod, attr),
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

	public static HookOrder CreateOrder(string ownerID, string? orderDomain, int localPriority, MethodInfo patchMethod, int ordinal, string prefix) {
		if (patchMethod.DeclaringType?.FullName is null)
			throw new InvalidOperationException("expected patch method to have a declaring type with a fully-qualified name");
		string domain = string.IsNullOrWhiteSpace(orderDomain) ? ownerID : ownerID + "::" + orderDomain;
		string localID = prefix + ":" + patchMethod.DeclaringType.FullName + "." + patchMethod.Name + "#" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
		return new HookOrder(domain, localID, localPriority);
	}

	public static HookOrder CreateOrder(string ownerID, IHookAttribute attr, MethodInfo patchMethod, int ordinal, string prefix) =>
		CreateOrder(ownerID, attr.OrderDomain, attr.LocalPriority, patchMethod, ordinal, prefix);

	private static DetourConfig detourConfigFor(string ownerID, MethodInfo patchMethod, IHookAttribute attr) => new(
		id: attr.DetourIDOverride ?? autoDetourIDFor(ownerID, patchMethod),
		priority: attr.DetourPriority,
		before: attr.DetourBefore,
		after: attr.DetourAfter
	);

	private static string autoDetourIDFor(string ownerID, MethodInfo patchMethod) {
		if (patchMethod.DeclaringType?.FullName is null)
			throw new InvalidOperationException("expected patch method to have a declaring type with a fully-qualified name");
		return $"{ownerID}::{patchMethod.DeclaringType.FullName}.{patchMethod.Name}";
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
