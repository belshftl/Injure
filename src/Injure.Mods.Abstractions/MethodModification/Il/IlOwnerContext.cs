// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Information regarding the owner that an IL authoring object was handed out to.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and describes an environment with no loaded mods
/// and no declared dependencies by the current owner.
/// </remarks>
internal readonly struct IlOwnerContext {
	public bool IsValid => ModInfo is not null;

	public FrozenDictionary<string, (string OwnerId, bool IsReloadable)> ModInfo => field ?? FrozenDictionary<string, (string OwnerId, bool IsReloadable)>.Empty;
	public FrozenSet<string> DeclaredDependencies => field ?? [];

	public IlOwnerContext(IReadOnlyDictionary<string, (string OwnerId, bool isReloadable)> modInfo, IReadOnlySet<string> declaredDependencies) {
		InternalStateException.ThrowIfNull(modInfo);
		InternalStateException.ThrowIfNull(declaredDependencies);
		ModInfo = modInfo.ToFrozenDictionary(StringComparer.Ordinal);
		foreach ((string ownerId, _) in ModInfo.Values)
			InternalStateException.ThrowIfInvalidOwnerId(ownerId);
		DeclaredDependencies = declaredDependencies.ToFrozenSet(StringComparer.Ordinal);
		foreach (string ownerId in DeclaredDependencies)
			InternalStateException.ThrowIfInvalidOwnerId(ownerId);
	}

	/// <summary>
	/// Whether a type scope names a mod that can be reloaded.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Only an assembly scope can. A module scope names the module a reference was decoded from, which
	/// on the patching path is the target module itself; a module reference names a netmodule of the
	/// target's own assembly. Neither can reach a mod, because a mod is a separate assembly and a
	/// reference to one is always assembly-scoped.
	/// </para>
	/// <para>
	/// The two non-assembly arms are safe rather than merely unhandled. A module-scoped reference
	/// either resolves to a <c>TypeDef</c> in the target module or fails at encode, since the resolver
	/// will not invent a <c>ModuleRef</c>; a module reference can only name a module the target already
	/// references. So a reference that somehow carried a mod's module identity is refused with a clear
	/// message at encode time instead of silently passing this check.
	/// </para>
	/// </remarks>
	public bool IsReloadable(IlTypeScope scope) => scope switch {
		IlTypeScope.Assembly asm =>
			ModInfo.TryGetValue(asm.Identity.Name, out (string _, bool IsReloadable) info) && info.IsReloadable,
		IlTypeScope.Module or IlTypeScope.ModuleReference => false,
		_ => throw new InternalStateException($"unknown IlTypeScope derived type '{scope.GetType()}'"),
	};
}
