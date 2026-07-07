// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.Abstractions.Hooks.Il;

/// <summary>
/// Collection of every <see cref="IlMatch"/> returned from one manipulation snapshot.
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

	public int Count => ranges.Length;

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

	public Enumerator GetEnumerator() => new(core, ranges);

	public ref struct Enumerator {
		private readonly IlTransactionCore core;
		private readonly IlRange[] ranges;
		private int index;

		internal Enumerator(IlTransactionCore core, IlRange[] ranges) {
			this.core = core;
			this.ranges = ranges;
			index = -1;
		}

		public readonly IlMatch Current => new(core, ranges[index]);

		public bool MoveNext() {
			int next = index + 1;
			if ((uint)next >= (uint)ranges.Length)
				return false;
			index = next;
			return true;
		}
	}
}
