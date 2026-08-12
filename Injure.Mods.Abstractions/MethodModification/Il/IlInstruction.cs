// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.MethodModification.Il;

internal readonly record struct IlInstruction(
	IlInstructionId Id,
	ILOpCode OpCode,
	IlOperand Operand,
	IlInstructionPrefixes? Prefixes,
	int OriginalOffset,
	InternalIlProvenance Provenance
) {
	public const int NoOriginalOffset = -1;
	public bool IsPrefixed => Prefixes is not null;
	public override string ToString() {
		string prefix = Prefixes is null ? "" : Prefixes + " ";
		return ReferenceEquals(Operand, IlNoneOperand.Instance) ? $"{Id}: {prefix}{OpCode}" : $"{Id}: {prefix}{OpCode} {Operand}";
	}
}
