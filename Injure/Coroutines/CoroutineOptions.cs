// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Coroutines;

public sealed class CoroutineOptions {
	public string? Name { get; init; }
	public int MaxStepsPerTick { get; init; } = 1024;
}
