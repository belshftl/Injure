// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions;

/// <summary>
/// Marker interface used to declare the lifetime identity of a mod.
/// </summary>
/// <remarks>
/// <para>
/// In this mod-loading framework, every mod has a <i>lifetime identity.</i> Its purpose is to serve
/// as a "representative" empty type for a particular mod, and is not meant to be used as anything
/// other than a generic type argument.
/// </para>
/// <para>
/// Consider a "generation-bounded cancellation token" type: a cancellation token that is guaranteed
/// to fire at latest when the mod's generation ends. The problem here, though, is what the "mod" in
/// question is. Presumably, it'd be the mod that minted the token, but there's no real way to tell
/// what mod one belongs to other than by a runtime check; some method that takes in a bounded
/// cancellation token for a given mod would have to validate that it doesn't belong to another mod.
/// The solution is rather unorthodox for the .NET ecosystem: bake in a type representing the mod as
/// a type parameter. That way, <c>BoundedCancellationToken&lt;FirstModL&gt;</c> and
/// <c>BoundedCancellationToken&lt;SecondModL&gt;</c> are incompatible types at compile time. Sure
/// enough, this is what the real <see cref="BoundedCt{L}"/> type does.
/// </para>
/// <para>
/// See <c>docs/mods/lifetime-identity.md</c> and <c>docs/conventions/type-parameters.md</c> for
/// more information.
/// </para>
/// <para>
/// Implementations must be <see langword="struct"/>s marked with
/// <see cref="ModLifetimeIdentityBelongsToAttribute"/>, as enforced by both the analyzer and the
/// mod runtime. The analyzer additionally enforces that the target struct:
/// <list type="bullet">
/// <item><description>is a <c>readonly struct</c>,</description></item>
/// <item><description>is not a <c>ref struct</c>,</description></item>
/// <item><description>is not nested inside another type,</description></item>
/// <item><description>is not generic, including closed generics,</description></item>
/// <item><description>is <see langword="public"/>,</description></item>
/// <item><description>and contains no members.</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IModLifetimeIdentity;

/// <summary>
/// Declares the owner of a lifetime identity.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ModLifetimeIdentityBelongsToAttribute(string ownerId) : Attribute {
	/// <summary>
	/// Owner ID of the mod that the lifetime identity belongs to.
	/// </summary>
	public string OwnerId { get; } = ownerId;
}
