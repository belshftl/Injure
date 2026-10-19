// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// A module's <c>ModuleID</c>, not to be confused with an MVID.
/// </summary>
/// <remarks>
/// <para>
/// Opaque. A <c>ModuleID</c> is just an opaque pointer that's not meaningful in any way other than
/// comparisons against other <c>ModuleID</c>s. See <see cref="ModuleInfo.Mvid"/> to match against
/// metadata read from disk.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is no ID: it refers to no module, and
/// <see cref="IsValid"/> is <see langword="false"/> for it.
/// </para>
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
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
internal readonly record struct ModuleInfo(ModuleId Id, string Path, Guid Mvid, bool IsCollectible) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => Path is null;
}
