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

public readonly struct LoadedCodeDepInfo<L, LDep> where L : struct, IModLifetimeIdentity where LDep : struct, IModLifetimeIdentity {
	public string OwnerId { get; }
	public Semver Version { get; }
	public ReloadGeneration Generation { get; }
	public IBoundedScope<LDep> Scope { get; }
	public IModExportTable<L, LDep> Exports { get; }
	public Assembly Assembly { get; }

	internal LoadedCodeDepInfo(
		string ownerId,
		Semver version,
		ReloadGeneration generation,
		IBoundedScope<LDep> scope,
		IModExportTable<L, LDep> exports,
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
