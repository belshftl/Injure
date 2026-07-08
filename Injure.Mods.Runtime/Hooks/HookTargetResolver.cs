// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Injure.Mods.Abstractions;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.Hooks;

internal sealed class HookTargetResolver : IStrongRefDroppable {
	private Dictionary<string, HookTargetDefinition>? targets = new(StringComparer.Ordinal);

	public HookTargetResolver() {
	}

	public HookTargetResolver(IEnumerable<Assembly> assemblies) {
		foreach (Assembly assembly in assemblies)
			AddStoreAssembly(assembly);
	}

	public void AddStoreAssembly(Assembly assembly) {
		ArgumentNullException.ThrowIfNull(assembly);
		chk();
		foreach (ModHookTargetStoreAttribute attr in assembly.GetCustomAttributes<ModHookTargetStoreAttribute>()) {
			if (attr.StoreType.Assembly != assembly)
				throw new HookValidationException($"hook target store type '{attr.StoreType.FullName}' does not belong to attributed assembly '{assembly.FullName}'");
			addStore(attr.StoreType);
		}
	}

	public bool TryResolve(string targetId, out HookTargetDefinition target) {
		chk();
		return targets.TryGetValue(targetId, out target);
	}

	public HookTargetDefinition Resolve(string targetId) {
		chk();
		ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
		if (targets.TryGetValue(targetId, out HookTargetDefinition target))
			return target;
		throw new MissingMethodException($"could not resolve hook target '{targetId}'");
	}

	public void DropStrongReferences() {
		targets?.Clear();
		targets = null;
	}

	private void addStore(Type storeType) {
		MethodInfo enumerate = storeType.GetMethod(
			"Enumerate",
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
			binder: null,
			types: Type.EmptyTypes,
			modifiers: null
		) ?? throw new HookValidationException(
			$"hook target store '{storeType.FullName}' must expose static IEnumerable<HookTargetDefinition> Enumerate()"
		);

		if (!typeof(IEnumerable<HookTargetDefinition>).IsAssignableFrom(enumerate.ReturnType))
			throw new HookValidationException($"hook target store '{storeType.FullName}'.Enumerate() must return IEnumerable<HookTargetDefinition>");

		var values = (IEnumerable<HookTargetDefinition>)enumerate.Invoke(null, null)!;
		foreach (HookTargetDefinition target in values) {
			if (string.IsNullOrWhiteSpace(target.TargetId))
				throw new HookValidationException($"hook target store '{storeType.FullName}' returned a target with a null/empty/whitespace id");
			if (target.Method is null)
				throw new HookValidationException($"hook target store '{storeType.FullName}' returned target '{target.TargetId}' with a null method");
			if (target.NextDelegateType is null)
				throw new HookValidationException($"hook target store '{storeType.FullName}' returned target '{target.TargetId}' with a null next delegate type");
			if (!targets!.TryAdd(target.TargetId, target))
				throw new HookValidationException($"hook target store '{storeType.FullName}' returned duplicate hook target id '{target.TargetId}'");
		}
	}

	[MemberNotNull(nameof(targets))]
	private void chk() {
		if (targets is null)
			throw new InternalStateException("hook target resolver strong refs have already been dropped");
	}
}
