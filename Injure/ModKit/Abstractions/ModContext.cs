// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Core;
using Injure.ModKit.Abstractions.MonoMod;

namespace Injure.ModKit.Abstractions;

public interface IModContext<out TGameApi, L> where L : struct, IModLifetimeIdentity {
	string OwnerID { get; }
	Semver Version { get; }
	TGameApi Api { get; }
	IOwnerDiagnostics Diagnostics { get; }
	IBoundedScope<L> Scope { get; }
	ReloadGeneration Generation { get; }
	DiagnosticsSinkRegistry DiagnosticsSinkRegistry { get; }
}

public interface IModLoadContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	IModHookDeclarations<L> LoadHooks { get; }
	IModExportDeclarations<L> Exports { get; }
}

public interface IModLinkContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	// TODO: IModHookDeclarations<L> LinkHooks { get; }

	bool TryGetDependency(string ownerID, out LoadedDepInfo<L> info);
	bool TryGetCodeDependency(string ownerID, out UntypedLoadedCodeDepInfo<L> info);
	bool TryGetCodeDependency<LDependency>(out LoadedCodeDepInfo<L, LDependency> info) where LDependency : struct, IModLifetimeIdentity;

	LoadedDepInfo<L> RequireDependency(string ownerID);
	UntypedLoadedCodeDepInfo<L> RequireCodeDependency(string ownerID);
	LoadedCodeDepInfo<L, LDependency> RequireCodeDependency<LDependency>() where LDependency : struct, IModLifetimeIdentity;
}

public interface IModActivateContext<out TGameApi, L> : IModContext<TGameApi, L> where L : struct, IModLifetimeIdentity {
	GameServices GameServices { get; }
	IBoundedScope<L> ActivationScope { get; }
}

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
