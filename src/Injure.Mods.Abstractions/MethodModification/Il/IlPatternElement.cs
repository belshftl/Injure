// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Reflection.Metadata;
using Injure.Mods.Abstractions.MethodModification.Il.Metadata;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Opaque value describing one instruction match pattern in an IL match pattern.
/// </summary>
/// <remarks>
/// <para>
/// Instances are normally created through <see cref="MatchIl"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct IlPatternElement {
	internal enum PatternKind : byte {
		UninitializedValue,
		Any,
		OpCode,
		Instruction,
	}

	internal PatternKind Kind { get; }
	internal ILOpCode OpCodeValue { get; }
	internal IlOperand Operand { get; }
	internal bool IsValid => Kind != PatternKind.UninitializedValue;

	internal IlPatternElement(PatternKind kind, ILOpCode opCode = default, IlOperand? operand = null) {
		Kind = kind;
		OpCodeValue = IlOpCodeInfo.Canonicalize(opCode);
		Operand = operand ?? IlNoneOperand.Instance;
	}
}
