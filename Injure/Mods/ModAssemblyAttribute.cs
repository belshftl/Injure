// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.Mods;

/// <summary>
/// Specifies the highest reload capability supported by a mod assembly.
/// </summary>
/// <remarks>
/// <para>
/// The numeric values of this enum are part of the mod assembly ABI.
/// </para>
/// <para>
/// This value describes assembly capability rather than a request for a particular
/// reload boundary. The manifest must declare a compatible reloadability level.
/// </para>
/// </remarks>
// this enum's numeric values are also mirrored in Injure.Mods.Analyzers/Core/Model.cs
public enum ModAssemblyHotReloadLevel {
	/// <summary>
	/// Reload is not supported. A process restart is required to replace,
	/// enable, or disable the mod.
	/// </summary>
	None = 1,

	/// <summary>
	/// Reload is partially supported. The assembly may be reloaded at a safe reload
	/// boundary, but does not support preserving live state across a reload.
	/// </summary>
	SafeBoundary = 2,

	/// <summary>
	/// Reload is supported. The assembly may be reloaded at a safe or live boundary,
	/// and live state is saved and restored.
	/// </summary>
	Live = 3,
}

/// <summary>
/// Declares this assembly to be a mod assembly.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one instance of this attribute is required on every code mod entry assembly.
/// The declared values are validated against the mod manifest and discovered entrypoint
/// types before mod code is invoked.
/// </para>
/// <para>
/// <paramref name="lifetimeIdentityType"/> must be a struct implementing
/// <see cref="IModLifetimeIdentity"/> and marked with a
/// <see cref="ModLifetimeIdentityBelongsToAttribute"/> whose owner ID matches
/// <paramref name="ownerID"/>.
/// </para>
/// <para>
/// The lifetime identity may be declared in a referenced shared contract assembly.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ModAssemblyAttribute(string ownerID, ModAssemblyHotReloadLevel hotReloadLevel, Type lifetimeIdentityType) : Attribute {
	/// <summary>
	/// Owner ID of the mod that the assembly belongs to.
	/// </summary>
	public string OwnerID { get; } = ownerID;

	/// <summary>
	/// Highest reload capability supported by the mod.
	/// </summary>
	public ModAssemblyHotReloadLevel HotReloadLevel { get; } = hotReloadLevel;

	/// <summary>
	/// The mod's lifetime identity type.
	/// </summary>
	public Type LifetimeIdentityType { get; } = lifetimeIdentityType;
}
