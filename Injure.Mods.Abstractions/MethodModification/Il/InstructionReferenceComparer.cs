// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Mono.Cecil.Cil;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal sealed class InstructionReferenceComparer : IEqualityComparer<Instruction> {
	public static InstructionReferenceComparer Instance { get; } = new();
	private InstructionReferenceComparer() {
	}
	public bool Equals(Instruction? x, Instruction? y) => ReferenceEquals(x, y);
	public int GetHashCode(Instruction obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
