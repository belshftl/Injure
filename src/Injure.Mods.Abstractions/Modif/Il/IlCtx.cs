// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Transaction-scoped view of the method body presented to an IL manipulator.
/// </summary>
/// <typeparam name="L">
/// Lifetime identity of the owner; see <c>Docs/mods/lifetime-identity.md</c> for more info.
/// </typeparam>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly ref struct IlCtx<L> where L : struct, IModLifetimeIdentity {
	internal IlTransactionCore Core => field ?? throw new InvalidOperationException("this IlCtx<L> value is uninitialized/invalid");

	internal IlCtx(IlTransactionCore core) {
		InternalStateException.ThrowIfNull(core);
		Core = core;
	}

	/// <summary>
	/// The amount of instructions the manipulator can see, and therefore the highest valid boundary index.
	/// Unaffected by anything emitted during this transaction.
	/// </summary>
	public int InstructionCount => Core.InstructionCount;

	/// <summary>
	/// Creates an unresolved transaction-local label that can be used by branch emissions before it
	/// is marked.
	/// </summary>
	public IlLabel DefineLabel() => Core.DefineLabel();

	/// <summary>
	/// Formats the method body, including the current uncommitted edits, for debug purposes.
	/// </summary>
	/// <param name="options">
	/// Formatting options. If you just want a debug log into the terminal, a good bet is
	/// <see cref="IlFormatPreset.Debug"/>.
	/// </param>
	/// <remarks>
	/// <para>
	/// You'll usually just use this like:
	/// <code>
	/// MyMod.Log.Debug(ctx.Display(IlFormatPreset.Debug));
	/// </code>
	/// Or even just a plain <c>Console.WriteLine</c>.
	/// </para>
	/// <para>
	/// The output includes the transaction, the method's locals, and its body (including labels and
	/// exception regions). Instructions are annotated with their boundary indices (as numbers in the
	/// gutter) and provenance (as comments). The uncommitted edits from this manipulator are marked.
	/// </para>
	/// <para>
	/// The output is not meant to be machine-parsed, and changes to its format will not be treated
	/// as an API break. It is meant to be read by a human, e.g. as a debug dump or from a logfile.
	/// </para>
	/// </remarks>
	public string Display(in IlFormatOptions options) => Core.Display(in options);

	/// <summary>
	/// Emits a fragment before the first instruction of the method.
	/// </summary>
	/// <remarks>
	/// The original first instruction keeps its anchor, so an exception region or branch that began at
	/// the start of the method still begins there, after the emitted fragment. The fragment therefore
	/// runs on entry but sits outside any protected region that started at the first instruction.
	/// </remarks>
	public void EmitAtStart(IlEmitAction emit) => Core.EmitAtBoundary(0, emit);

	/// <summary>
	/// Emits a fragment after the last instruction of the method.
	/// </summary>
	/// <remarks>
	/// A well-formed method ends with a terminal instruction, so a fragment emitted here is normally
	/// unreachable. That is allowed: unreachable instructions are not rejected, though they still
	/// contribute to the method's computed stack height. To run code before a method returns, match its
	/// terminal instructions (usually <c>ret</c> / <c>throw</c>) and emit before each one.
	/// </remarks>
	public void EmitAtEnd(IlEmitAction emit) => Core.EmitAtBoundary(Core.InstructionCount, emit);

	/// <summary>
	/// Finds every occurrence of a non-empty pattern in the snapshot that matches the given
	/// provenance constraint.
	/// </summary>
	/// <remarks>
	/// Matching without a provenance constraint is not allowed; if provenance is irrelevant, use
	/// <see cref="IlProvenanceConstr.Any"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="pattern"/> is empty, or if <paramref name="provenance"/> is an
	/// invalid/uninitialized value.
	/// </exception>
	public IlMatches MatchAll(ReadOnlySpan<IlPatternElement> pattern, IlProvenanceConstr provenance) =>
		Core.MatchAll(pattern, provenance);

	/// <summary>
	/// Finds the first occurrence of a non-empty pattern in the snapshot that matches the given
	/// provenance constraint.
	/// </summary>
	/// <remarks>
	/// Matching without a provenance constraint is not allowed; if provenance is irrelevant, use
	/// <see cref="IlProvenanceConstr.Any"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="pattern"/> is empty, or if <paramref name="provenance"/> is an
	/// invalid/uninitialized value.
	/// </exception>
	/// <exception cref="IlMatchException">
	/// Thrown if no match is found.
	/// </exception>
	public IlMatch MatchNext(ReadOnlySpan<IlPatternElement> pattern, IlProvenanceConstr provenance) =>
		Core.MatchNext(0, pattern, provenance);
}
