// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// A module's <c>ModuleID</c>, not to be confused with an MVID.
/// </summary>
/// <remarks>
/// Opaque. A <c>ModuleID</c> is just an opaque pointer that's not meaningful in any way other than
/// comparisons against other <c>ModuleID</c>s. See <see cref="ModuleInfo.Mvid"/> to match against
/// metadata read from disk.
/// </remarks>
internal readonly record struct ModuleId(nuint Value) {
	public bool IsValid => Value != 0;
	public override string ToString() => $"module 0x{Value:x}";
}

/// <summary>
/// What the profiler knows about a loaded module.
/// </summary>
/// <param name="Id">The module's <c>ModuleID</c>, not to be confused with an MVID.</param>
/// <param name="Path">The module's on-disk path, or an empty string for a module with no file.</param>
/// <param name="Mvid">The module version ID, usable for matching against metadata read from disk.</param>
/// <param name="IsCollectible">Whether the module belongs to a collectible load context.</param>
internal readonly record struct ModuleInfo(ModuleId Id, string Path, Guid Mvid, bool IsCollectible);
