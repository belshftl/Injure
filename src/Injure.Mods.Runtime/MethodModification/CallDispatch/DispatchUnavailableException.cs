// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Runtime.MethodModification.CallDispatch;

/// <summary>
/// Indicates that patched code reached an indirect dispatch slot whose target is gone.
/// </summary>
/// <remarks>
/// Exists because the alternative is returning a null pointer and <c>calli</c>ing null is a segfault.
/// </remarks>
public sealed class DispatchUnavailableException(int slot, string? target) : InvalidOperationException(
	target is null
		? $"dispatch slot {slot} has no target; perhaps the code trying to use it is running a version of a method that has since been reverted?"
		: $"dispatch slot {slot} targeted '{target}', which has since been unloaded"
) {
	public int Slot { get; } = slot;
}
