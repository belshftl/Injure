// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.Mods;

/// <summary>
/// Marker interface used to declare the lifetime identity of a mod.
/// </summary>
/// <remarks>
/// <para>
/// See <c>Docs/mods/lifetime-identity.md</c> for information on lifetime identities, their usages aside
/// from purely being compile-time guards, and cross-mod interactions.
/// </para>
/// <para>
/// Implementations must be structs marked with <see cref="ModLifetimeIdentityBelongsToAttribute"/>,
/// as enforced by both the analyzer and the runtime.
/// The analyzer additionally enforces that the target struct:
/// <list type="bullet">
/// <item><description>is a <c>readonly struct</c>,</description></item>
/// <item><description>is not a <c>ref struct</c>,</description></item>
/// <item><description>is not nested inside another type,</description></item>
/// <item><description>is not generic, including closed generics,</description></item>
/// <item><description>is public,</description></item>
/// <item><description>and contains no members.</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IModLifetimeIdentity {
}

/// <summary>
/// Declares the owner of a lifetime identity.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ModLifetimeIdentityBelongsToAttribute(string ownerID) : Attribute {
	/// <summary>
	/// Owner ID of the mod that the lifetime identity belongs to.
	/// </summary>
	public string OwnerID { get; } = ownerID;
}
