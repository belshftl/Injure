// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods;

public readonly record struct ReloadGeneration(string OwnerId, ulong Value) {
	public override string ToString() => $"{OwnerId}@{Value:D4}";
}

public sealed class ReloadGenerationExpiredException(ReloadGeneration? generation)
	: InvalidOperationException($"object belongs to expired reload generation {generation?.ToString() ?? "<unknown>"}") {
	public ReloadGeneration? Generation { get; } = generation;
}
