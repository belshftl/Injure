// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Runtime.MethodModification.Profiler;

/// <summary>
/// A method identity, using specific metadata that both sides of the boundary agree on.
/// </summary>
/// <remarks>
/// A <c>MethodDef</c> token is stable for a module's lifetime and can be obtained from <c>MetadataToken</c> on
/// <see cref="System.Reflection.MethodBase"/>, so there's no need for e.g. a translation table.
/// <c>FunctionID</c> would not be sufficient, since it's per-instantiation for generic methods, whereas ReJIT
/// operates on the definition and covers every instantiation at once.
/// </remarks>
internal readonly record struct MethodIdentity(ModuleId Module, int MethodDefToken) {
	public override string ToString() => $"{Module}!0x{MethodDefToken:x8}";
}
