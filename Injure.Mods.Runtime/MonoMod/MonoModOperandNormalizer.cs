// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using MonoMod.Cil;
using Injure.Mods.Abstractions.Hooks.Il;

namespace Injure.Mods.Runtime.MonoMod;

internal sealed class MonoModOperandNormalizer : IIlBackendOperandNormalizer {
	public static readonly MonoModOperandNormalizer Instance = new();
	private MonoModOperandNormalizer() {
	}
	public object? NormalizeOperand(object? operand) => operand switch {
		ILLabel label => label.Target,
		ILLabel[] labels => labels.Select(static l => l.Target).ToArray(),
		_ => operand,
	};
}
