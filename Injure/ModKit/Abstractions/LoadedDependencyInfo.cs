// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace Injure.ModKit.Abstractions;

public readonly struct LoadedDepInfo {
	public string OwnerID { get; }
	public Semver Version { get; }
	public ReloadGeneration Generation { get; }
	public IUntypedBoundedScope Scope { get; }

	internal LoadedDepInfo(string ownerID, Semver version, ReloadGeneration generation, IUntypedBoundedScope scope) {
		OwnerID = ownerID;
		Version = version;
		Generation = generation;
		Scope = scope;
	}
}

public readonly struct UntypedLoadedCodeDepInfo {
	public Type LifetimeIdentityType { get; }
	public string OwnerID { get; }
	public Semver Version { get; }
	public ReloadGeneration Generation { get; }
	public IUntypedBoundedScope Scope { get; }
	public Assembly Assembly { get; }

	internal UntypedLoadedCodeDepInfo(Type lifetimeIdentityType, string ownerID, Semver version, ReloadGeneration generation, IUntypedBoundedScope scope, Assembly assembly) {
		LifetimeIdentityType = lifetimeIdentityType;
		OwnerID = ownerID;
		Version = version;
		Generation = generation;
		Scope = scope;
		Assembly = assembly;
	}
}

public readonly struct LoadedCodeDepInfo<LDependency> where LDependency : struct, IModLifetimeIdentity {
	public string OwnerID { get; }
	public Semver Version { get; }
	public ReloadGeneration Generation { get; }
	public IBoundedScope<LDependency> Scope { get; }
	public Assembly Assembly { get; }

	internal LoadedCodeDepInfo(string ownerID, Semver version, ReloadGeneration generation, IBoundedScope<LDependency> scope, Assembly assembly) {
		OwnerID = ownerID;
		Version = version;
		Generation = generation;
		Scope = scope;
		Assembly = assembly;
	}
}
