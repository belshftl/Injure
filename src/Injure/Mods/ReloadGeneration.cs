// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods;

/// <summary>
/// One reload generation of an owner, i.e. one of the successive lifetimes of a reloadable mod
/// between reloads.
/// </summary>
/// <param name="OwnerId">The owner whose generation this is.</param>
/// <param name="Value">The generation number.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct ReloadGeneration(string OwnerId, ulong Value) : IHasInvalidDefault {
	/// <summary>
	/// Returns <c>owner@number</c>, with the number padded to 4 digits (e.g. 0001), or
	/// <c>default(ReloadGeneration)</c> for the <see langword="default"/> value.
	/// </summary>
	public override string ToString() => OwnerId is null ? "default(ReloadGeneration)" : $"{OwnerId}@{Value:D4}";

	bool IHasInvalidDefault.IsDefault => OwnerId is null;
}

public sealed class ReloadGenerationExpiredException(ReloadGeneration? generation)
	: InvalidOperationException($"object belongs to expired reload generation {generation?.ToString() ?? "<unknown>"}") {
	public ReloadGeneration? Generation { get; } = chk(generation);

	private static ReloadGeneration? chk(ReloadGeneration? generation) {
		ArgumentInvalidDefaultException.ThrowIfDefault(generation);
		return generation;
	}
}
