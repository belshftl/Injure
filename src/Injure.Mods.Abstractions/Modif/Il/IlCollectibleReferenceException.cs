// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Exception thrown when an IL authoring API would produce an illegal reference from a
/// non-collectible assembly to a collectible one, usually from a game to a reloadable mod, since it
/// would otherwise fail later at type load or JIT time.
/// </summary>
/// <remarks>
/// <para>
/// Type token operands' types (<c>castclass</c>, <c>box</c>, <c>unbox.any</c>, <c>newarr</c>,
/// <c>sizeof</c>, etc.) and member operands' declaring types (<c>call</c>, <c>newobj</c>,
/// <c>ldfld</c>, <c>ldtoken</c>, etc.) cannot reference collectible ALCs.
/// No further nuance or exemptions were observed (see below notes on how this was derived).
/// </para>
/// <para>
/// Signature operands' types (<c>calli</c>, field signatures, the locals header, etc.) may
/// reference a collectible ALC as long as there is no need for the JIT to resolve the layout of the
/// type. This means that:
/// <list type="bullet">
/// <item><description>reference types,</description></item>
/// <item><description>byrefs/pointers to value types,</description></item>
/// <item><description>arrays of value types,</description></item>
/// <item><description>and generic reference types with value-type type arguments</description></item>
/// </list>
/// are all fine, but not plain value types or generic value types with value-type type arguments.
/// </para>
/// <para>
/// Function pointer signatures are exempt from the above rule; since the type is always
/// function-pointer-sized, the signature is not resolved by the JIT, and as such function pointers
/// may name value types from collectible ALCs by value.
/// </para>
/// <para>
/// The behavior described above was derived empirically under CoreCLR 10.0.11 with a profiler.
/// Only the coarse "non-collectible assembly may not hold a static reference to a type in a
/// collectible one" is contractual, and the rest (type tokens vs signatures, the function pointer
/// exemption, generic instantiation behavior) is not specified anywhere and is emergent behavior
/// from when exactly CoreCLR is forced to materialize a type handle. A future runtime's behavior
/// changes may therefore cause an API break (and, promptly, a major version bump); notably,
/// <see href="https://learn.microsoft.com/en-us/dotnet/fundamentals/reflection/collectible-assemblies#lifetime-of-collectible-assemblies">
/// the docs explicitly state that the assembly a closed/constructed generic type is considered to
/// belong to is an implementation detail subject to change.
/// </see>
/// </para>
/// <para>
/// This exception is not required for correctness in the sense that UB or other corruption would
/// occur without it; it's merely a means for IL authoring APIs to fail eagerly for the cases they
/// can detect ahead of time. The downstream failure is a <see cref="FileLoadException"/> wrapping a
/// <see cref="NotSupportedException"/>, stating "<c>A non-collectible assembly may not reference a
/// collectible assembly.</c>", thrown when the patched method is invoked rather than at patch time.
/// </para>
/// </remarks>
public sealed class IlCollectibleReferenceException : IlPipelineException {
	internal IlCollectibleReferenceException(string message) : base(message) {
	}
}
