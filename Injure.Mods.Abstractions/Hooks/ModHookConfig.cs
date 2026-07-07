// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Hooks;

public readonly struct ModHookConfig {
	public required string LocalId { get; init; }

	public string? LocalOrderDomain { get; init; }
	public int LocalPriority { get; init; }

	public IReadOnlyList<OwnerOrderingConstraint>? Before { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? After { get; init; }
}
