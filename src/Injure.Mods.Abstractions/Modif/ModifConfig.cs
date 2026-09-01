// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif;

public readonly struct DetourConfig {
	public required string LocalId { get; init; }
	public int LocalPriority { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? Before { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? After { get; init; }
}

public readonly struct PatchConfig {
	public required string LocalId { get; init; }
	public int LocalPriority { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? Before { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? After { get; init; }
}
