// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Runtime.MethodModification;

/// <remarks>
/// The <see langword="default"/> value is valid and is equivalent to <see cref="None"/>.
/// </remarks>
internal readonly record struct MethodGeneration(int Patch, bool HasDetourPrologue) {
	public static readonly MethodGeneration None = default;
	public bool IsModified => Patch > 0 || HasDetourPrologue;
	public override string ToString() => HasDetourPrologue ? $"patch {Patch}, detoured" : $"patch {Patch}";
}
