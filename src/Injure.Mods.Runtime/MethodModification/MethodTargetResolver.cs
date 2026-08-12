// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Injure.Mods.Abstractions;
using Injure.Mods.Weaver;

namespace Injure.Mods.Runtime.MethodModification;

internal sealed class MethodTargetResolver : IStrongRefDroppable {
	private Dictionary<string, MethodTargetDefinition>? targets = new(StringComparer.Ordinal);

	public MethodTargetResolver() {
	}

	public MethodTargetResolver(IEnumerable<Assembly> assemblies) {
		foreach (Assembly assembly in assemblies)
			AddStoreAssembly(assembly);
	}

	public void AddStoreAssembly(Assembly assembly) {
		InternalStateException.ThrowIfNull(assembly);
		chk();
		foreach (ModMethodTargetStoreAttribute attr in assembly.GetCustomAttributes<ModMethodTargetStoreAttribute>()) {
			if (attr.StoreType.Assembly != assembly)
				throw new MethodModificationValidationException(
					$"method target store type '{attr.StoreType.FullName}' does not belong to attributed assembly '{assembly.FullName}'"
				);
			add(attr.StoreType);
		}
	}

	public bool TryResolve(string targetId, out MethodTargetDefinition target) {
		chk();
		return targets.TryGetValue(targetId, out target);
	}

	public MethodTargetDefinition Resolve(string targetId) {
		chk();
		ArgumentException.ThrowIfNullOrWhiteSpace(targetId); // todo figure out whether this should be InternalStateException i'm tired rn
		if (targets.TryGetValue(targetId, out MethodTargetDefinition target))
			return target;
		throw new MissingMethodException($"could not resolve method target '{targetId}'");
	}

	public void DropStrongReferences() {
		targets?.Clear();
		targets = null;
	}

	private void add(Type storeType) {
		MethodInfo enumerate = storeType.GetMethod(
			"Enumerate",
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
			binder: null,
			types: Type.EmptyTypes,
			modifiers: null
		) ?? throw new MethodModificationValidationException($"method target store '{storeType.FullName}' must expose static IEnumerable<MethodTargetDefinition> Enumerate()");
		if (!typeof(IEnumerable<MethodTargetDefinition>).IsAssignableFrom(enumerate.ReturnType))
			throw new MethodModificationValidationException($"method target store '{storeType.FullName}'.Enumerate() must return IEnumerable<MethodTargetDefinition>");
		var values = (IEnumerable<MethodTargetDefinition>)enumerate.Invoke(null, null)!;
		foreach (MethodTargetDefinition target in values) {
			if (string.IsNullOrWhiteSpace(target.TargetId))
				throw new MethodModificationValidationException($"method target store '{storeType.FullName}' returned a target with a null/empty/whitespace id");
			if (target.Method is null)
				throw new MethodModificationValidationException($"method target store '{storeType.FullName}' returned target '{target.TargetId}' with a null method");
			if (target.NextDelegateType is null)
				throw new MethodModificationValidationException(
					$"method target store '{storeType.FullName}' returned target '{target.TargetId}' with a null next delegate type"
				);
			if (!targets!.TryAdd(target.TargetId, target))
				throw new MethodModificationValidationException($"method target store '{storeType.FullName}' returned duplicate target id '{target.TargetId}'");
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(targets))]
	private void chk() {
		if (targets is null)
			throw new InternalStateException("method target resolver strong references have already been dropped");
	}
}
