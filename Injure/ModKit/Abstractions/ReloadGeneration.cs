// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

public readonly record struct ReloadGeneration(string OwnerID, ulong Value) {
	public override string ToString() => $"{OwnerID}@{Value:D4}";
}

public sealed class ReloadGenerationExpiredException(ReloadGeneration? generation)
	: InvalidOperationException($"object belongs to expired reload generation {generation?.ToString() ?? "<unknown>"}") {
	public ReloadGeneration? Generation { get; } = generation;
}
