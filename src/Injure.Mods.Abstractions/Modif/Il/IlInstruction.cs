// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using Injure.Mods.Abstractions.Modif.Il.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// One instruction of an <see cref="IlMethodBody"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpCode"/> is always a canonical encoding; the constructor validates that. Whether the
/// instruction is also a canonical instruction in the normative definition's sense depends on
/// <see cref="Operand"/>, which isn't checked here beyond being non-null: its kind has to match the
/// opcode, and unless it's a branch or switch target, the value the encoder writes for it (for a
/// metadata reference, the token it resolves to) has to be in the domain of the encoding.
/// </para>
/// <para>
/// In practice, that holds for every instruction the decoder produces and every instruction emitted
/// through <see cref="IlEmitter"/>, which checks operand kinds and index ranges. A metadata
/// reference only becomes a token at encode time, and the encoder rejects an unresolvable one with
/// <see cref="IlEncodingException"/>.
/// </para>
/// </remarks>
internal readonly struct IlInstruction : IEquatable<IlInstruction> {
	public const int NoOriginalOffset = -1;

	public IlInstructionId Id { get; }
	public ILOpCode OpCode { get; }
	public IlOperand Operand { get; }
	public IlInstructionPrefixes? Prefixes { get; }
	public int OriginalOffset { get; }
	public InternalIlProvenance Provenance { get; }

	public bool IsPrefixed => Prefixes is not null;

	public IlInstruction(
		IlInstructionId id,
		ILOpCode opCode,
		IlOperand operand,
		IlInstructionPrefixes? prefixes,
		int originalOffset,
		InternalIlProvenance provenance
	) {
		IlOpCodeDescriptor descriptor = IlOpCodeInfo.GetDescriptor(opCode);
		if (!descriptor.IsDefined || descriptor.Prefix != IlPrefixKind.None || descriptor.Canonical != opCode)
			throw new InternalStateException($"instruction {id} has opcode {opCode}, which isn't a canonical encoding");
		InternalStateException.ThrowIfNull(operand);
		Id = id;
		OpCode = opCode;
		Operand = operand;
		Prefixes = prefixes;
		OriginalOffset = originalOffset;
		Provenance = provenance;
	}

	public IlInstruction WithOperand(IlOperand operand) =>
		new(Id, OpCode, operand, Prefixes, OriginalOffset, Provenance);

	public IlInstruction WithProvenance(InternalIlProvenance provenance) =>
		new(Id, OpCode, Operand, Prefixes, OriginalOffset, provenance);

	public bool Equals(IlInstruction other) =>
		Id == other.Id && OpCode == other.OpCode && Operand == other.Operand && Prefixes == other.Prefixes
		&& OriginalOffset == other.OriginalOffset && Provenance == other.Provenance;
	public override bool Equals([NotNullWhen(true)] object? obj) => obj is IlInstruction other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Id, OpCode, Operand, Prefixes, OriginalOffset, Provenance);
	public static bool operator ==(IlInstruction left, IlInstruction right) => left.Equals(right);
	public static bool operator !=(IlInstruction left, IlInstruction right) => !left.Equals(right);

	public override string ToString() {
		string prefix = Prefixes is null ? "" : Prefixes + " ";
		return ReferenceEquals(Operand, IlNoneOperand.Instance) ? $"{Id}: {prefix}{OpCode}" : $"{Id}: {prefix}{OpCode} {Operand}";
	}
}
