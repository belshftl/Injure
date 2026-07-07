// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Hooks.Il;

/// <summary>
/// Stack-bound half-open range of matched instructions in a manipulator snapshot.
/// </summary>
public readonly ref struct IlMatch {
	private readonly IlTransactionCore core;
	private readonly IlRange range;
	internal IlMatch(IlTransactionCore core, IlRange range) {
		this.core = core;
		this.range = range;
	}

	/// <summary>
	/// Amount of instructions in this match.
	/// </summary>
	public int InstructionCount => range.Length;

	/// <summary>
	/// Emits a fragment before the matched range.
	/// </summary>
	public void EmitBefore(IlEmitAction emit) => core.EmitAtBoundary(range.Start, emit);

	/// <summary>
	/// Emits a fragment after the matched range.
	/// </summary>
	public void EmitAfter(IlEmitAction emit) => core.EmitAtBoundary(range.End, emit);

	/// <summary>
	/// Emits a fragment at an offset relative to before the matched range.
	/// </summary>
	/// <remarks>
	/// Offset zero is before the first instruction and offset <see cref="InstructionCount"/> is
	/// after the last instruction.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="offset"/> is outside of the matched range.
	/// </exception>
	public void EmitAt(int offset, IlEmitAction emit) {
		if ((uint)offset > (uint)range.Length)
			throw new ArgumentOutOfRangeException(nameof(offset));
		core.EmitAtBoundary(range.Start + offset, emit);
	}

	/// <summary>
	/// Finds the next pattern occurring strictly after this match.
	/// </summary>
	/// <exception cref="IlMatchException">
	/// Thrown if no match is found.
	/// </exception>
	public IlMatch MatchNext(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) => core.MatchNext(range.End, pattern, provenance);

	/// <summary>
	/// Finds the nearest preceding pattern ending no later than the start of this match.
	/// </summary>
	/// <exception cref="IlMatchException">
	/// Thrown if no match is found.
	/// </exception>
	public IlMatch MatchPrev(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) => core.MatchPrev(range.Start, pattern, provenance);

	/// <summary>
	/// Gets the provenance of one instruction in the matched range.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="offset"/> is outside of the matched range.
	/// </exception>
	public IlProvenance GetProvenance(int offset) {
		if ((uint)offset >= (uint)range.Length)
			throw new ArgumentOutOfRangeException(nameof(offset));
		return core.GetProvenance(range.Start + offset);
	}

	/// <summary>
	/// Attempts to get one provenance value shared by every instruction in the matched range.
	/// </summary>
	public bool TryGetUniformProvenance(out IlProvenance provenance) {
		if (range.Length == 0) {
			provenance = default;
			return false;
		}
		IlProvenance first = core.GetProvenance(range.Start);
		for (int i = range.Start + 1; i < range.End; i++) {
			if (core.GetProvenance(i) != first) {
				provenance = default;
				return false;
			}
		}
		provenance = first;
		return true;
	}
}
