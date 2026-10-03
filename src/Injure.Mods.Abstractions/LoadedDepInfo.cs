// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace Injure.Mods.Abstractions;

/// <summary>
/// Information about a loaded dependency (either a content mod or a code mod), including its scope.
/// </summary>
/// <typeparam name="L">
/// Lifetime identity of the mod this object was handed out to, i.e. the dependent mod.
/// </typeparam>
/// <remarks>
/// For a code mod, prefer <see cref="LoadedCodeDepInfo{L, LDep}"/>, which also gives access to its
/// exports, or <see cref="UntypedLoadedCodeDepInfo{L}"/>.
/// </remarks>
public readonly struct LoadedDepInfo<L> where L : struct, IModLifetimeIdentity {
	/// <summary>
	/// The dependency's owner ID.
	/// </summary>
	public string OwnerId { get; }

	/// <summary>
	/// The dependency's version.
	/// </summary>
	public Semver Version { get; }

	/// <summary>
	/// The dependency's current generation.
	/// </summary>
	public ReloadGeneration Generation { get; }

	/// <summary>
	/// Scope for the dependency's current generation.
	/// </summary>
	public IUntypedBoundedScope Scope { get; }

	internal LoadedDepInfo(string ownerId, Semver version, ReloadGeneration generation, IUntypedBoundedScope scope) {
		OwnerId = ownerId;
		Version = version;
		Generation = generation;
		Scope = scope;
	}
}

/// <summary>
/// Information about a loaded code mod dependency whose lifetime identity isn't known at compile
/// time, including its scope and assembly.
/// </summary>
/// <typeparam name="L">
/// Lifetime identity of the mod this object was handed out to, i.e. the dependent mod.
/// </typeparam>
/// <remarks>
/// <para>
/// Prefer <see cref="LoadedCodeDepInfo{L, LDep}"/>, especially for a dependency that has exports or
/// otherwise exports its lifetime identity publicly through a contract assembly.
/// </para>
/// <para>
/// There is intentionally no untyped way to access exports, as using a mod's exports requires a
/// compile-time reference to its contract assembly anyway.
/// </para>
/// </remarks>
public readonly struct UntypedLoadedCodeDepInfo<L> where L : struct, IModLifetimeIdentity {
	/// <summary>
	/// The dependency's lifetime identity type. Implements <see cref="IModLifetimeIdentity"/>.
	/// </summary>
	public Type LifetimeIdentityType { get; }

	/// <inheritdoc cref="LoadedDepInfo{L}.OwnerId"/>
	public string OwnerId { get; }

	/// <inheritdoc cref="LoadedDepInfo{L}.Version"/>
	public Semver Version { get; }

	/// <inheritdoc cref="LoadedDepInfo{L}.Generation"/>
	public ReloadGeneration Generation { get; }

	/// <inheritdoc cref="LoadedDepInfo{L}.Scope"/>
	public IUntypedBoundedScope Scope { get; }

	/// <summary>
	/// The dependency's entry assembly in its current generation.
	/// </summary>
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

/// <summary>
/// Information about a loaded code mod dependency, including its scope, exports, and assembly.
/// </summary>
/// <typeparam name="L">
/// Lifetime identity of the mod this object was handed out to, i.e. the dependent mod.
/// </typeparam>
/// <typeparam name="LDep">Lifetime identity of the dependency.</typeparam>
public readonly struct LoadedCodeDepInfo<L, LDep> where L : struct, IModLifetimeIdentity where LDep : struct, IModLifetimeIdentity {
	/// <inheritdoc cref="LoadedDepInfo{L}.OwnerId"/>
	public string OwnerId { get; }

	/// <inheritdoc cref="LoadedDepInfo{L}.Version"/>
	public Semver Version { get; }

	/// <inheritdoc cref="LoadedDepInfo{L}.Generation"/>
	public ReloadGeneration Generation { get; }

	/// <inheritdoc cref="LoadedDepInfo{L}.Scope"/>
	public IBoundedScope<LDep> Scope { get; }

	/// <summary>
	/// The exports the dependency declared during its load.
	/// </summary>
	public IModExportTable<L, LDep> Exports { get; }

	/// <inheritdoc cref="UntypedLoadedCodeDepInfo{L}.Assembly"/>
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
