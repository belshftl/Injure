// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.MethodModification;

public readonly struct ModDetourConfig {
	public required string LocalId { get; init; }
	public int LocalPriority { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? Before { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? After { get; init; }
}

public readonly struct ModPatchConfig {
	public required string LocalId { get; init; }
	public int LocalPriority { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? Before { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? After { get; init; }
}
