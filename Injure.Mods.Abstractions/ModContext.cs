// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.CodeAnalysis;
using Injure.Mods.Abstractions.Hooks;
using Injure.Runtime;

namespace Injure.Mods.Abstractions;

[DontCache]
[DontImplement]
public interface IModContext<out TGameApi, L> where L : struct, IModLifetimeIdentity {
	string OwnerId { get; }
	Semver Version { get; }
	TGameApi Api { get; }
	IOwnerDiagnostics Diagnostics { get; }
	IBoundedScope<L> Scope { get; }
	ReloadGeneration Generation { get; }
	DiagnosticsSinkRegistry DiagnosticsSinkRegistry { get; }
}

[DontCache]
[DontImplement]
public interface IModLoadContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IModHookDeclarations<L> LoadHooks { get; }
	IModExportDeclarations<L> Exports { get; }
}

[DontCache]
[DontImplement]
public interface IModLinkContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	// TODO: IModHookDeclarations<L> LinkHooks { get; }

	bool TryGetDependency(string ownerId, out LoadedDepInfo<L> info);
	bool TryGetCodeDependency(string ownerId, out UntypedLoadedCodeDepInfo<L> info);
	bool TryGetCodeDependency<LDependency>(out LoadedCodeDepInfo<L, LDependency> info) where LDependency : struct, IModLifetimeIdentity;

	LoadedDepInfo<L> RequireDependency(string ownerId);
	UntypedLoadedCodeDepInfo<L> RequireCodeDependency(string ownerId);
	LoadedCodeDepInfo<L, LDependency> RequireCodeDependency<LDependency>() where LDependency : struct, IModLifetimeIdentity;
}

[DontCache]
[DontImplement]
public interface IModActivateContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	GameServices GameServices { get; }
	IBoundedScope<L> ActivationScope { get; }
}

[DontCache]
[DontImplement]
public interface IModReloadContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IReadOnlySet<string> ReloadSet { get; }
	GameServices? GameServices { get; }
}

public sealed class ModLifecycleContextExpiredException : Exception {
	public string Kind { get; }
	public ReloadGeneration Generation { get; }
	internal ModLifecycleContextExpiredException(string kind, ReloadGeneration generation) : base(
		$"{kind} object for {generation} is no longer valid past the method return; cache the values you'd like to keep around such as Api/Scope/Diagnostics, don't retain or capture the whole object"
	) {
		Kind = kind;
		Generation = generation;
	}
}
