// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Modif.Il;

/// <summary>
/// Half-open range of matched instructions in a manipulator snapshot.
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
	/// Offset zero is before the first instruction and offset <see cref="InstructionCount"/> is after
	/// the last instruction.
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
	public IlMatch MatchNext(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) =>
		core.MatchNext(range.End, pattern, provenance);

	/// <summary>
	/// Finds the nearest preceding pattern ending no later than the start of this match.
	/// </summary>
	/// <exception cref="IlMatchException">
	/// Thrown if no match is found.
	/// </exception>
	public IlMatch MatchPrev(ReadOnlySpan<IlPatternElement> pattern, IlPatternProvenanceConstraint provenance) =>
		core.MatchPrev(range.Start, pattern, provenance);

	/// <summary>
	/// Gets the provenance of one instruction in the matched range.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="offset"/> is outside of the matched range.
	/// </exception>
	public IlProvenance GetProvenance(int offset) {
		if ((uint)offset >= (uint)range.Length)
			throw new ArgumentOutOfRangeException(nameof(offset));
		return core.GetProvenance(range.Start + offset).ToPublic();
	}

	/// <summary>
	/// Attempts to get one owner provenance value shared by every instruction in the matched range.
	/// </summary>
	/// <remarks>
	/// Behaves differently from <see cref="IlPatternProvenanceConstraint.AllUniform"/>; if all of the
	/// matched instructions have unknown provenance, this returns <see langword="true"/> and sets
	/// <paramref name="provenance"/> to the unknown-provenance value.
	/// </remarks>
	public bool TryGetUniformProvenance(out IlProvenance provenance) {
		if (range.Length == 0) {
			provenance = default;
			return false;
		}
		InternalIlProvenance first = core.GetProvenance(range.Start);
		for (int i = range.Start + 1; i < range.End; i++)
			if (core.GetProvenance(i).OwnerIdIndex != first.OwnerIdIndex) {
				provenance = default;
				return false;
			}
		provenance = first.ToPublic();
		return true;
	}
}

/// <summary>
/// Set of non-overlapping <see cref="IlMatch"/>es.
/// </summary>
public readonly ref struct IlMatches {
	private readonly IlTransactionCore core;
	private readonly IlRange[] ranges;
	private readonly IlPatternSearchInfo info;

	internal IlMatches(IlTransactionCore core, IlRange[] ranges, IlPatternSearchInfo info) {
		this.core = core;
		this.ranges = ranges;
		this.info = info;
	}

	/// <summary>
	/// Gets the number of matching ranges.
	/// </summary>
	public int Count => ranges.Length;

	/// <summary>
	/// Gets a matching range by index.
	/// </summary>
	public IlMatch this[int index] => new(core, ranges[index]);

	/// <summary>
	/// Gets the only match in this collection.
	/// </summary>
	/// <exception cref="IlMatchException">
	/// Thrown unless the collection contains exactly one match.
	/// </exception>
	public IlMatch RequireSingle() {
		if (ranges.Length != 1)
			throw IlMatchException.ExpectedOne(in info, ranges.Length);
		return new IlMatch(core, ranges[0]);
	}

	/// <summary>
	/// Attempts to get the only match in this collection.
	/// </summary>
	public bool TryGetSingle(out IlMatch match) {
		if (ranges.Length == 1) {
			match = new IlMatch(core, ranges[0]);
			return true;
		}
		match = default;
		return false;
	}

	/// <summary>
	/// Returns an allocation-free enumerator over the <see cref="IlMatch"/>es.
	/// </summary>
	public Enumerator GetEnumerator() => new(core, ranges);

	/// <summary>
	/// Enumerates <see cref="IlMatch"/>es.
	/// </summary>
	public ref struct Enumerator {
		private readonly IlTransactionCore core;
		private readonly IlRange[] ranges;
		private int index;

		internal Enumerator(IlTransactionCore core, IlRange[] ranges) {
			this.core = core;
			this.ranges = ranges;
			index = -1;
		}

		/// <summary>
		/// Gets the current <see cref="IlMatch"/>.
		/// </summary>
		public readonly IlMatch Current => new(core, ranges[index]);

		/// <summary>
		/// Advances to the next <see cref="IlMatch"/>.
		/// </summary>
		public bool MoveNext() => ++index < ranges.Length;
	}
}
