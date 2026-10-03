// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Running;

namespace Injure.Mods.Abstractions.Benchmarks.Modif.Il;

internal static class Program {
	private static void Main(string[] args) =>
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
