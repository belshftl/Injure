// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Collections;

/// <summary>
/// A <typeparamref name="TLeft"/> &lt;-&gt; <typeparamref name="TRight"/> bijection that internally
/// keeps an immutable snapshot, which reads atomically read and writes atomically replace wholly.
/// </summary>
/// <typeparam name="TLeft">Type of the left side of a pair.</typeparam>
/// <typeparam name="TRight">Type of the right side of a pair.</typeparam>
/// <remarks>
/// <para>
/// A read atomically queries the current snapshot, so it never blocks or observes a torn mutation.
/// A write does the same, but then copies the whole snapshot, applies the change, freezes the result
/// into a new snapshot, and atomically publishes it, so <b>any write is O(n)</b> regardless of the
/// amount of pair it touches.
/// </para>
/// <para>
/// As expected by the nature of snapshot publication, <b>the supported concurrency model is
/// single-writer multiple-reader</b>. Concurrent writers technically can't corrupt the map, since
/// each published snapshot is internally consistent and publication is atomic, but they do clobber
/// each other's changes.
/// </para>
/// <para>
/// Whether a mutation that changes nothing still publishes a new snapshot is unspecified.
/// </para>
/// </remarks>
public sealed class FrozenSnapshotBijectiveMap<TLeft, TRight>
	: IReadOnlyBijectiveMap<TLeft, TRight> where TLeft : notnull where TRight : notnull {
	// ==========================================================================
	// abstraction over l/r pairs
	private readonly ref struct PairSource {
		private enum Kind : byte {
			PairSpan,
			SplitSpans,
		}

		private readonly ReadOnlySpan<(TLeft Left, TRight Right)> pairs;
		private readonly ReadOnlySpan<TLeft> lefts;
		private readonly ReadOnlySpan<TRight> rights;
		private readonly Kind kind;

		public PairSource(ReadOnlySpan<(TLeft Left, TRight Right)> pairs) {
			this.pairs = pairs;
			lefts = default;
			rights = default;
			kind = Kind.PairSpan;
		}

		public PairSource(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) {
			if (lefts.Length != rights.Length)
				throw new ArgumentException("left/right counts must match");
			pairs = default;
			this.lefts = lefts;
			this.rights = rights;
			kind = Kind.SplitSpans;
		}

		public int Length => kind == Kind.PairSpan ? pairs.Length : lefts.Length;
		public (TLeft left, TRight right) Get(int idx) => kind == Kind.PairSpan
			? pairs[idx]
			: (lefts[idx], rights[idx]);
	}

	// ==========================================================================
	// bookkeeping
	private sealed class Snapshot(FrozenDictionary<TLeft, TRight> ltr, FrozenDictionary<TRight, TLeft> rtl) {
		public readonly FrozenDictionary<TLeft, TRight> Ltr = ltr;
		public readonly FrozenDictionary<TRight, TLeft> Rtl = rtl;

		public static readonly Snapshot Empty = new(
			FrozenDictionary<TLeft, TRight>.Empty,
			FrozenDictionary<TRight, TLeft>.Empty
		);
	}

	private readonly IEqualityComparer<TLeft>? cmpLeft;
	private readonly IEqualityComparer<TRight>? cmpRight;
	private Snapshot snapshot;

	// ==========================================================================
	// ctors

	/// <summary>
	/// Creates an empty map.
	/// </summary>
	public FrozenSnapshotBijectiveMap(
		IEqualityComparer<TLeft>? cmpLeft = null,
		IEqualityComparer<TRight>? cmpRight = null
	) {
		snapshot = Snapshot.Empty;
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	/// <summary>
	/// Creates a map with <paramref name="pairs"/> as the contents.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if multiple pairs share a left value or share a right value.
	/// </exception>
	public FrozenSnapshotBijectiveMap(
		ReadOnlySpan<(TLeft, TRight)> pairs,
		IEqualityComparer<TLeft>? cmpLeft = null,
		IEqualityComparer<TRight>? cmpRight = null
	) {
		snapshot = mksnap(new PairSource(pairs), cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	/// <summary>
	/// Creates a map with <paramref name="lefts"/> and <paramref name="rights"/> zipped as the contents.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="lefts"/> and <paramref name="rights"/> differ in length, or if multiple
	/// pairs share a left value or share a right value.
	/// </exception>
	public FrozenSnapshotBijectiveMap(
		ReadOnlySpan<TLeft> lefts,
		ReadOnlySpan<TRight> rights,
		IEqualityComparer<TLeft>? cmpLeft = null,
		IEqualityComparer<TRight>? cmpRight = null
	) {
		snapshot = mksnap(new PairSource(lefts, rights), cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	// ==========================================================================
	// private utility
	private static Snapshot freeze(
		Dictionary<TLeft, TRight> ltr,
		Dictionary<TRight, TLeft> rtl,
		IEqualityComparer<TLeft>? cmpLeft,
		IEqualityComparer<TRight>? cmpRight
	) => new(
		ltr.Count == 0 ? FrozenDictionary<TLeft, TRight>.Empty : ltr.ToFrozenDictionary(cmpLeft),
		rtl.Count == 0 ? FrozenDictionary<TRight, TLeft>.Empty : rtl.ToFrozenDictionary(cmpRight)
	);

	private static void add(
		Dictionary<TLeft, TRight> ltr,
		Dictionary<TRight, TLeft> rtl,
		TLeft left,
		TRight right
	) {
		if (ltr.ContainsKey(left))
			throw new ArgumentException("duplicate left key");
		if (rtl.ContainsKey(right))
			throw new ArgumentException("duplicate right key");
		ltr.Add(left, right);
		rtl.Add(right, left);
	}

	private static void setBijection(
		Dictionary<TLeft, TRight> ltr,
		Dictionary<TRight, TLeft> rtl,
		TLeft left,
		TRight right
	) {
		if (ltr.TryGetValue(left, out TRight? oldRight)) {
			if (rtl.Comparer.Equals(oldRight, right))
				return;
			rtl.Remove(oldRight);
		}
		if (rtl.TryGetValue(right, out TLeft? oldLeft))
			if (!ltr.Comparer.Equals(oldLeft, left))
				ltr.Remove(oldLeft);
		ltr[left] = right;
		rtl[right] = left;
	}

	private static Snapshot mksnap(
		IEnumerable<(TLeft, TRight)> pairs,
		IEqualityComparer<TLeft>? cmpLeft,
		IEqualityComparer<TRight>? cmpRight
	) {
		Dictionary<TLeft, TRight> ltr = new(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(cmpRight);
		foreach ((TLeft left, TRight right) in pairs)
			add(ltr, rtl, left, right);
		return freeze(ltr, rtl, cmpLeft, cmpRight);
	}

	private static Snapshot mksnap(
		PairSource pairs,
		IEqualityComparer<TLeft>? cmpLeft,
		IEqualityComparer<TRight>? cmpRight
	) {
		Dictionary<TLeft, TRight> ltr = new(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			add(ltr, rtl, left, right);
		}
		return freeze(ltr, rtl, cmpLeft, cmpRight);
	}

	// ==========================================================================
	// enumerator

	/// <summary>
	/// Enumerates the pairs of one snapshot.
	/// </summary>
	/// <remarks>
	/// The snapshot is captured when the enumerator is created, so subsequent mutations of the map
	/// neither invalidate the enumerator nor affect what it yields.
	/// </remarks>
	public struct Enumerator : IEnumerator<(TLeft Left, TRight Right)> {
		private readonly FrozenDictionary<TLeft, TRight> ltr;
		private FrozenDictionary<TLeft, TRight>.Enumerator inner;

		internal Enumerator(FrozenDictionary<TLeft, TRight> ltr) {
			this.ltr = ltr;
			inner = ltr.GetEnumerator();
		}

		public readonly (TLeft Left, TRight Right) Current {
			get {
				KeyValuePair<TLeft, TRight> kvp = inner.Current;
				return (kvp.Key, kvp.Value);
			}
		}
		readonly object IEnumerator.Current => Current;

		public bool MoveNext() => inner.MoveNext();

		public void Reset() => inner = ltr.GetEnumerator();

		/// <summary>
		/// Does nothing.
		/// </summary>
		public readonly void Dispose() {
		}
	}

	// ==========================================================================
	// IReadOnlyBijectiveMap

	/// <summary>
	/// Number of pairs in the current snapshot.
	/// </summary>
	public int Count {
		get {
			Snapshot s = Volatile.Read(ref snapshot);
			return s.Ltr.Count;
		}
	}

	/// <summary>
	/// Enumerates the pairs of the snapshot current as of this call, in unspecified order.
	/// </summary>
	public Enumerator GetEnumerator() => new(Volatile.Read(ref snapshot).Ltr);
	IEnumerator<(TLeft Left, TRight Right)> IEnumerable<(TLeft Left, TRight Right)>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <inheritdoc/>
	public bool ContainsLeft(TLeft left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Ltr.ContainsKey(left);
	}

	/// <inheritdoc/>
	public bool ContainsRight(TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Rtl.ContainsKey(right);
	}

	/// <inheritdoc/>
	public TRight GetByLeft(TLeft left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Ltr[left];
	}

	/// <inheritdoc/>
	public TLeft GetByRight(TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Rtl[right];
	}

	/// <inheritdoc/>
	public bool TryGetByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Ltr.TryGetValue(left, out right);
	}

	/// <inheritdoc/>
	public bool TryGetByRight(TRight right, [NotNullWhen(true)] out TLeft? left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Rtl.TryGetValue(right, out left);
	}

	// ==========================================================================
	// mutation

	/// <summary>
	/// Publishes an empty snapshot.
	/// </summary>
	public void Clear() => Volatile.Write(ref snapshot, Snapshot.Empty);

	/// <summary>
	/// Adds the pair (<paramref name="left"/> &lt;-&gt; <paramref name="right"/>).
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if either value already belongs to a pair. No new snapshot is published.
	/// </exception>
	public void Add(TLeft left, TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (s.Ltr.ContainsKey(left))
			throw new InvalidOperationException("this left key is already in the map");
		if (s.Rtl.ContainsKey(right))
			throw new InvalidOperationException("this right key is already in the map");
		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		setBijection(ltr, rtl, left, right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	/// <summary>
	/// Adds the pair (<paramref name="left"/> &lt;-&gt; <paramref name="right"/>) unless either value
	/// already belongs to a pair.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the pair was added.
	/// </returns>
	public bool TryAdd(TLeft left, TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (s.Ltr.ContainsKey(left) || s.Rtl.ContainsKey(right))
			return false;
		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		setBijection(ltr, rtl, left, right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	/// <summary>
	/// Forces the pair (<paramref name="left"/> &lt;-&gt; <paramref name="right"/>) into the map,
	/// removing any existing pairs that held either of the two values.
	/// </summary>
	/// <remarks>
	/// Up to two existing pairs can get removed this way, one holding <paramref name="left"/> and one
	/// holding <paramref name="right"/>.
	/// </remarks>
	public void Set(TLeft left, TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		setBijection(ltr, rtl, left, right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	/// <summary>
	/// Removes the pair with this left value, if there is one.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if a pair was removed.
	/// </returns>
	public bool RemoveByLeft(TLeft left) => RemoveByLeft(left, out _);

	/// <summary>
	/// Removes the pair with this left value, if there is one.
	/// </summary>
	/// <param name="left">Left value of the pair to remove.</param>
	/// <param name="right">
	/// Corresponding, now-removed right value, or <see langword="default"/> if there was none.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if a pair was removed.
	/// </returns>
	public bool RemoveByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (!s.Ltr.TryGetValue(left, out right))
			return false;
		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		ltr.Remove(left);
		rtl.Remove(right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	/// <summary>
	/// Removes the pair with this right value, if there is one.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if a pair was removed.
	/// </returns>
	public bool RemoveByRight(TRight right) => RemoveByRight(right, out _);

	/// <summary>
	/// Removes the pair with this right value, if there is one.
	/// </summary>
	/// <param name="right">Right value of the pair to remove.</param>
	/// <param name="left">
	/// Corresponding, now-removed left value, or <see langword="default"/> if there was none.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if a pair was removed.
	/// </returns>
	public bool RemoveByRight(TRight right, [NotNullWhen(true)] out TLeft? left) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (!s.Rtl.TryGetValue(right, out left))
			return false;
		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		ltr.Remove(left);
		rtl.Remove(right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	// ==========================================================================
	// bulk mutation

	/// <summary>
	/// Discards the current contents and replaces them with <paramref name="pairs"/>, as one snapshot
	/// publication.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if multiple pairs share a left value or share a right value. No new snapshot is published.
	/// </exception>
	public void ReplaceContents(IEnumerable<(TLeft, TRight)> pairs) {
		Snapshot @new = mksnap(pairs, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	/// <summary>
	/// Discards the current contents and replaces them with <paramref name="pairs"/>, as one snapshot
	/// publication.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if multiple pairs share a left value or share a right value. No new snapshot is published.
	/// </exception>
	public void ReplaceContents(ReadOnlySpan<(TLeft, TRight)> pairs) {
		Snapshot @new = mksnap(new PairSource(pairs), cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	/// <summary>
	/// Discards the current contents and replaces them with <paramref name="lefts"/> and <paramref name="rights"/>
	/// zipped, as one snapshot publication.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="lefts"/> and <paramref name="rights"/> differ in length, or if multiple
	/// pairs share a left value or share a right value. No new snapshot is published.
	/// </exception>
	public void ReplaceContents(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) {
		Snapshot @new = mksnap(new PairSource(lefts, rights), cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	private void add(PairSource pairs) {
		Snapshot s = Volatile.Read(ref snapshot);
		HashSet<TLeft> seenLeft = new(cmpLeft);
		HashSet<TRight> seenRight = new(cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			if (s.Ltr.ContainsKey(left))
				throw new InvalidOperationException($"one of the left keys is already in the map (index {i} in the given list)");
			if (s.Rtl.ContainsKey(right))
				throw new InvalidOperationException($"one of the right keys is already in the map (index {i} in the given list)");
			if (!seenLeft.Add(left))
				throw new ArgumentException($"duplicate left key in the given list (index {i})");
			if (!seenRight.Add(right))
				throw new ArgumentException($"duplicate right key in the given list (index {i})");
		}

		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			ltr.Add(left, right);
			rtl.Add(right, left);
		}
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	/// <summary>
	/// Adds every pair in <paramref name="pairs"/>, as one snapshot publication.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if multiple pairs share a left value or share a right value. No new snapshot is published.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if some pair holds a value that already belongs to an existing pair in the map. No new
	/// snapshot is published.
	/// </exception>
	public void Add(ReadOnlySpan<(TLeft, TRight)> pairs) => add(new PairSource(pairs));

	/// <summary>
	/// Adds every pair in <paramref name="lefts"/> and <paramref name="rights"/> zipped, as one
	/// snapshot publication.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="lefts"/> and <paramref name="rights"/> differ in length, or if multiple
	/// pairs share a left value or share a right value. No new snapshot is published.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if some pair holds a value that already belongs to an existing pair in the map. No new
	/// snapshot is published.
	/// </exception>
	public void Add(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) => add(new PairSource(lefts, rights));

	private void set(PairSource pairs) {
		Snapshot s = Volatile.Read(ref snapshot);
		HashSet<TLeft> seenLeft = new(cmpLeft);
		HashSet<TRight> seenRight = new(cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			if (!seenLeft.Add(left))
				throw new ArgumentException($"duplicate left key in the given list (index {i})");
			if (!seenRight.Add(right))
				throw new ArgumentException($"duplicate right key in the given list (index {i})");
		}

		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			setBijection(ltr, rtl, left, right);
		}
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	/// <summary>
	/// Forces every pair in <paramref name="pairs"/> into the map, removing any existing pairs that
	/// held held any of the values involved, as one snapshot publication.
	/// </summary>
	/// <remarks>
	/// Each given pair can remove up to two existing ones; see <see cref="Set(TLeft, TRight)"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if multiple pairs share a left value or share a right value (which would make the result
	/// dependent on the order of the inputs). No new snapshot is published.
	/// </exception>
	public void Set(ReadOnlySpan<(TLeft, TRight)> pairs) => set(new PairSource(pairs));

	/// <summary>
	/// Forces every pair in <paramref name="lefts"/> and <paramref name="rights"/> zipped into the map,
	/// removing any existing pairs that held held any of the values involved, as one snapshot publication.
	/// </summary>
	/// <remarks>
	/// Each given pair can remove up to two existing ones; see <see cref="Set(TLeft, TRight)"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="lefts"/> and <paramref name="rights"/> differ in length, or if multiple
	/// pairs share a left value or share a right value (which would make the result dependent on the
	/// order of the inputs). No new snapshot is published.
	/// </exception>
	public void Set(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) => set(new PairSource(lefts, rights));

	/// <summary>
	/// For each element of <paramref name="lefts"/>, removes the pair with that left value if there
	/// is one, all as one snapshot publication.
	/// </summary>
	/// <returns>
	/// How many pairs were removed.
	/// </returns>
	public int RemoveByLeft(ReadOnlySpan<TLeft> lefts) {
		Snapshot s = Volatile.Read(ref snapshot);
		bool any = false;
		foreach (TLeft left in lefts) {
			if (s.Ltr.ContainsKey(left)) {
				any = true;
				break;
			}
		}
		if (!any)
			return 0;

		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		int removed = 0;
		foreach (TLeft left in lefts) {
			if (!ltr.Remove(left, out TRight? right))
				continue;
			rtl.Remove(right);
			removed++;
		}
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return removed;
	}

	/// <summary>
	/// For each element of <paramref name="rights"/>, removes the pair with that right value if there
	/// is one, all as one snapshot publication.
	/// </summary>
	/// <returns>
	/// How many pairs were removed.
	/// </returns>
	public int RemoveByRight(ReadOnlySpan<TRight> rights) {
		Snapshot s = Volatile.Read(ref snapshot);
		bool any = false;
		foreach (TRight right in rights) {
			if (s.Rtl.ContainsKey(right)) {
				any = true;
				break;
			}
		}
		if (!any)
			return 0;

		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		int removed = 0;
		foreach (TRight right in rights) {
			if (!rtl.Remove(right, out TLeft? left))
				continue;
			ltr.Remove(left);
			removed++;
		}
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return removed;
	}
}
