// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif;

/// <summary>
/// How a detour is identified within its owner, and ordered relative to other detours of the same
/// method.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since <see cref="LocalId"/> is required.
/// </remarks>
public readonly struct DetourConfig : IHasInvalidDefault {
	public required string LocalId { get; init; }
	public int LocalOrder { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? Before { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? After { get; init; }

	bool IHasInvalidDefault.IsDefault => LocalId is null;
}

/// <summary>
/// How a patch is identified within its owner, and ordered relative to other patches of the same
/// method.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since <see cref="LocalId"/> is required.
/// </remarks>
public readonly struct PatchConfig : IHasInvalidDefault {
	public required string LocalId { get; init; }
	public int LocalOrder { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? Before { get; init; }
	public IReadOnlyList<OwnerOrderingConstraint>? After { get; init; }

	bool IHasInvalidDefault.IsDefault => LocalId is null;
}
