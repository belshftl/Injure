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
	/// <see cref="IlPatternProvenanceConstraint.Any"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="pattern"/> is empty, or if <paramref name="provenance"/> is an
	/// invalid/uninitialized value.
	/// </exception>
	public IlMatches MatchAll(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) =>
		Core.MatchAll(pattern, provenance);

	/// <summary>
	/// Finds the first occurrence of a non-empty pattern in the snapshot that matches the given
	/// provenance constraint.
	/// </summary>
	/// <remarks>
	/// Matching without a provenance constraint is not allowed; if provenance is irrelevant, use
	/// <see cref="IlPatternProvenanceConstraint.Any"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="pattern"/> is empty, or if <paramref name="provenance"/> is an
	/// invalid/uninitialized value.
	/// </exception>
	/// <exception cref="IlMatchException">
	/// Thrown if no match is found.
	/// </exception>
	public IlMatch MatchNext(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) =>
		Core.MatchNext(0, pattern, provenance);
}
