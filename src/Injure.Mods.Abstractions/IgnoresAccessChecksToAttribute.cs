// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

#pragma warning disable IDE0130 // namespace does not match directory layout

namespace System.Runtime.CompilerServices;

/// <summary>
/// Undocumented CoreCLR attribute that allows an assembly to bypass JIT-time access checks of
/// types/members from another assembly.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not actually declared in the BCL</b>, despite the namespace; it is declared by
/// <c>Injure.Mods.Abstractions</c>. This type does not exist in the BCL, and the JIT simply
/// checks that the assembly is marked with an attribute with the right fully-qualified name, so
/// any external assembly can define it.
/// </para>
/// <para>
/// This still doesn't let you access non-public types/members of the target assembly at compile time,
/// as the C# compiler doesn't understand it; it only skips JIT-time access checks.
/// </para>
/// <para>
/// Mods must declare this with the target assembly being the game in order to be able to access
/// publicized types/members, including generated modif targets. The loader rejects mods without such
/// an attribute declaration.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute {
	/// <summary>
	/// The target assembly, towards which JIT-time access checks should be suppressed.
	/// </summary>
	public string AssemblyName { get; } = assemblyName;
}
