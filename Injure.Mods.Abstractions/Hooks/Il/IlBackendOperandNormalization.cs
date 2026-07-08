// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Hooks.Il;

internal interface IIlBackendOperandNormalizer {
	object? NormalizeOperand(object? operand);
}

internal sealed class NullBackendOperandNormalizer : IIlBackendOperandNormalizer {
	public static readonly NullBackendOperandNormalizer Instance = new();
	private NullBackendOperandNormalizer() {
	}
	public object? NormalizeOperand(object? operand) => operand;
}
