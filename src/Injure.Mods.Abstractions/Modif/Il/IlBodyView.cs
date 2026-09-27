// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// What an uncommitted edit would do to a row.
/// </summary>
internal enum IlRowStatus : byte {
	/// <summary>
	/// An instruction already in the body, untouched by the transaction.
	/// </summary>
	Existing,

	/// <summary>
	/// An instruction the transaction would insert.
	/// </summary>
	Inserted,
}

/// <summary>
/// One instruction of a body, either existing or as it would be once a transaction is committed.
/// </summary>
/// <param name="Status">What the transaction would do to this row.</param>
/// <param name="Index">
/// The row's boundary index in the body it was read from (the transaction's snapshot, for a
/// transaction), or <c>-1</c> for an inserted row.
/// </param>
/// <param name="OpCode">The opcode, in canonical form.</param>
/// <param name="Operand">
/// The operand, or <see cref="IlNoneOperand"/> for a pending branch, whose targets are in
/// <paramref name="Labels"/> instead.
/// </param>
/// <param name="Labels">The label targets of a pending branch/switch, otherwise <see langword="null"/>.</param>
/// <param name="Prefixes">The prefixes, if any.</param>
/// <param name="Provenance">The provenance the row has, or would have once committed.</param>
/// <param name="IsBaseline">Whether the instruction was decoded from metadata rather than emitted.</param>
internal readonly record struct IlBodyRow(
	IlRowStatus Status,
	int Index,
	ILOpCode OpCode,
	IlOperand Operand,
	IlLabel[]? Labels,
	IlInstructionPrefixes? Prefixes,
	InternalIlProvenance Provenance,
	bool IsBaseline
) {
	public static IlBodyRow FromExisting(IlInstruction instr, int index) => new(
		IlRowStatus.Existing,
		index,
		instr.OpCode,
		instr.Operand,
		null,
		instr.Prefixes,
		instr.Provenance,
		instr.OriginalOffset != IlInstruction.NoOriginalOffset
	);
}

/// <summary>
/// A method body laid out as rows, either existing or with a transaction's uncommitted edits applied.
/// </summary>
internal sealed class IlBodyView {
	public required IlMethodRef Method { get; init; }

	/// <summary>
	/// <c>owner</c> or <c>owner::local</c> of the transaction whose pending edits are merged in, or
	/// <see langword="null"/> for a committed body.
	/// </summary>
	public required string? TransactionName { get; init; }

	public required ImmutableArray<IlTypeRef> Locals { get; init; }

	/// <summary>
	/// Debug "provenance" of <see cref="Locals"/>; see <see cref="IlMethodBody.LocalsProvenance"/>.
	/// </summary>
	public required ImmutableArray<InternalIlProvenance> LocalsProvenance { get; init; }

	/// <summary>
	/// How many leading <see cref="Locals"/> were decoded from metadata. Debug info only, same as
	/// <see cref="LocalsProvenance"/>.
	/// </summary>
	public required int BaselineLocalCount { get; init; }

	/// <summary>
	/// Locals the transaction would append after <see cref="Locals"/>.
	/// </summary>
	public required ImmutableArray<IlTypeRef> DeclaredLocals { get; init; }

	/// <summary>
	/// Debug "provenance" for who would introduce <see cref="DeclaredLocals"/>; see
	/// <see cref="IlMethodBody.LocalsProvenance"/>.
	/// </summary>
	public required InternalIlProvenance DeclaredLocalsProvenance { get; init; }

	/// <summary>
	/// Whether locals would be zeroed once committed.
	/// </summary>
	public required bool InitLocals { get; init; }

	public required List<IlBodyRow> Rows { get; init; }

	/// <summary>
	/// For each output boundary (one more than there are rows), the existing anchor that lands there,
	/// or <see langword="null"/> for a boundary only an inserted instruction starts.
	/// </summary>
	public required List<IlAnchorId?> AnchorAt { get; init; }

	/// <summary>
	/// Output boundary of every marked label.
	/// </summary>
	public required Dictionary<int, int> LabelBoundaries { get; init; }

	public required IReadOnlyList<IlExceptionRegion> ExceptionRegions { get; init; }

	public static IlBodyView FromBody(IlMethodBody body) {
		InternalStateException.ThrowIfNull(body);
		List<IlBodyRow> rows = new(body.Instructions.Count);
		for (int i = 0; i < body.Instructions.Count; i++)
			rows.Add(IlBodyRow.FromExisting(body.Instructions[i], i));
		return new IlBodyView {
			Method = body.Method,
			TransactionName = null,
			Locals = body.Locals,
			LocalsProvenance = body.LocalsProvenance,
			BaselineLocalCount = body.BaselineLocalCount,
			DeclaredLocals = [],
			DeclaredLocalsProvenance = default,
			InitLocals = body.InitLocals,
			Rows = rows,
			AnchorAt = body.Anchors.Select(static a => (IlAnchorId?)a).ToList(),
			LabelBoundaries = [],
			ExceptionRegions = body.ExceptionRegions,
		};
	}
}
