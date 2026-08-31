// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Runtime.Modif.Profiler;

/// <summary>
/// A module as the profiler identifies it.
/// </summary>
/// <remarks>
/// Opaque. The value is a <c>ModuleID</c>, which is a runtime address and is neither stable across
/// processes nor meaningful to compare against anything but another <see cref="ModuleId"/>. Use
/// <see cref="ModuleInfo.Mvid"/> to correlate a module with metadata read from disk.
/// </remarks>
internal readonly record struct ModuleId(ulong Value) {
	public bool IsValid => Value != 0;
	public override string ToString() => $"module 0x{Value:x}";
}

/// <summary>
/// What the profiler knows about a loaded module.
/// </summary>
/// <param name="Id">The profiler's handle for the module.</param>
/// <param name="Path">The module's on-disk path, or an empty string for a module with no file.</param>
/// <param name="Mvid">The module version ID, which correlates this module with metadata read from disk.</param>
/// <param name="IsCollectible">Whether the module belongs to a collectible load context.</param>
internal readonly record struct ModuleInfo(ModuleId Id, string Path, Guid Mvid, bool IsCollectible);
