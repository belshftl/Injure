// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace Injure.Mods.Abstractions;

public readonly struct LoadedDepInfo<L> where L : struct, IModLifetimeIdentity {
	public string OwnerId { get; }
	public Semver Version { get; }
	public ReloadGeneration Generation { get; }
	public IUntypedBoundedScope Scope { get; }

	internal LoadedDepInfo(string ownerId, Semver version, ReloadGeneration generation, IUntypedBoundedScope scope) {
		OwnerId = ownerId;
		Version = version;
		Generation = generation;
		Scope = scope;
	}
}

public readonly struct UntypedLoadedCodeDepInfo<L> where L : struct, IModLifetimeIdentity {
	public Type LifetimeIdentityType { get; }
	public string OwnerId { get; }
	public Semver Version { get; }
	public ReloadGeneration Generation { get; }
	public IUntypedBoundedScope Scope { get; }
	public Assembly Assembly { get; }

	internal UntypedLoadedCodeDepInfo(Type lifetimeIdentityType, string ownerId, Semver version, ReloadGeneration generation, IUntypedBoundedScope scope, Assembly assembly) {
		LifetimeIdentityType = lifetimeIdentityType;
		OwnerId = ownerId;
		Version = version;
		Generation = generation;
		Scope = scope;
		Assembly = assembly;
	}
}

public readonly struct LoadedCodeDepInfo<L, LDependency> where L : struct, IModLifetimeIdentity where LDependency : struct, IModLifetimeIdentity {
	public string OwnerId { get; }
	public Semver Version { get; }
	public ReloadGeneration Generation { get; }
	public IBoundedScope<LDependency> Scope { get; }
	public IModExportTable<L, LDependency> Exports { get; }
	public Assembly Assembly { get; }

	internal LoadedCodeDepInfo(
		string ownerId,
		Semver version,
		ReloadGeneration generation,
		IBoundedScope<LDependency> scope,
		IModExportTable<L, LDependency> exports,
		Assembly assembly
	) {
		OwnerId = ownerId;
		Version = version;
		Generation = generation;
		Scope = scope;
		Exports = exports;
		Assembly = assembly;
	}
}
