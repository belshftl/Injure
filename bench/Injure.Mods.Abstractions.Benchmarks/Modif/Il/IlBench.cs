// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Benchmarks.Modif.Il;

internal static class IlBench {
	public const string OwnerId = "bench";
}

[ModLifetimeIdentityBelongsTo(IlBench.OwnerId)]
internal readonly struct BenchL : IModLifetimeIdentity;
