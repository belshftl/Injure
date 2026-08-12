// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Mods.Abstractions.MethodModification.Il;

/// <summary>
/// Base type for semantic IL instruction operands.
/// </summary>
public abstract record IlOperand {
	private protected IlOperand() {
	}
}

/// <summary>
/// "No operand" sentinel for instructions with no operands (<c>nop</c>, <c>ret</c>, <c>pop</c>, etc.)
/// used in internal instruction encoding.
/// </summary>
internal sealed record IlNoneOperand : IlOperand {
	public static readonly IlNoneOperand Instance = new();
	private IlNoneOperand() {}
}

/// <summary>
/// An inline 32-bit integer operand.
/// </summary>
/// <param name="Value">The <see langword="int"/> value.</param>
public sealed record IlInt32Operand(int Value) : IlOperand;

/// <summary>
/// An inline 64-bit integer operand.
/// </summary>
/// <param name="Value">The <see langword="long"/> value.</param>
public sealed record IlInt64Operand(long Value) : IlOperand;

/// <summary>
/// An inline 32-bit floating-point operand.
/// </summary>
/// <param name="Value">The <see langword="float"/> value.</param>
public sealed record IlFloat32Operand(float Value) : IlOperand;

/// <summary>
/// An inline 64-bit floating-point operand.
/// </summary>
/// <param name="Value">The <see langword="double"/> value.</param>
public sealed record IlFloat64Operand(double Value) : IlOperand;

/// <summary>
/// A user string operand.
/// </summary>
/// <param name="Value">The <see langword="string"/> value.</param>
public sealed record IlStringOperand(string Value) : IlOperand;

/// <summary>
/// An IL argument index operand.
/// </summary>
/// <param name="Index">The IL argument index, including <c>this</c> for instance methods.</param>
public sealed record IlArgumentOperand(int Index) : IlOperand;

/// <summary>
/// An IL local variable index operand.
/// </summary>
/// <param name="Index">The local variable index.</param>
public sealed record IlLocalOperand(int Index) : IlOperand;

/// <summary>
/// A type reference operand.
/// </summary>
/// <param name="Type">The referenced type.</param>
public sealed record IlTypeOperand(IlTypeRef Type) : IlOperand;

/// <summary>
/// A method reference operand.
/// </summary>
/// <param name="Method">The referenced method.</param>
public sealed record IlMethodOperand(IlMethodRef Method) : IlOperand;

/// <summary>
/// A field reference operand.
/// </summary>
/// <param name="Field">The referenced field.</param>
public sealed record IlFieldOperand(IlFieldRef Field) : IlOperand;

/// <summary>
/// A standalone callsite signature operand.
/// </summary>
/// <param name="Signature">The callsite signature.</param>
public sealed record IlCallSiteOperand(IlMethodSignature Signature) : IlOperand;

/// <summary>
/// An internal branch target operand.
/// </summary>
/// <param name="Target">The target instruction boundary.</param>
internal sealed record IlBranchOperand(IlAnchorId Target) : IlOperand;

/// <summary>
/// An internal switch target operand.
/// </summary>
/// <param name="Targets">The target instruction boundaries.</param>
internal sealed record IlSwitchOperand(ImmutableArray<IlAnchorId> Targets) : IlOperand;
