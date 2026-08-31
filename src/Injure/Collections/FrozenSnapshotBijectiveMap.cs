// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Collections;

public sealed class FrozenSnapshotBijectiveMap<TLeft, TRight> : IBijectiveMap<TLeft, TRight> where TLeft : notnull where TRight : notnull {
	// ==========================================================================
	// abstraction over L/R pairs
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
		public (TLeft left, TRight right) Get(int idx) => kind == Kind.PairSpan ? pairs[idx] : (lefts[idx], rights[idx]);
	}

	// ==========================================================================
	// bookkeeping
	private sealed class Snapshot(FrozenDictionary<TLeft, TRight> ltr, FrozenDictionary<TRight, TLeft> rtl) {
		public readonly FrozenDictionary<TLeft, TRight> Ltr = ltr;
		public readonly FrozenDictionary<TRight, TLeft> Rtl = rtl;

		public static readonly Snapshot Empty = new(FrozenDictionary<TLeft, TRight>.Empty, FrozenDictionary<TRight, TLeft>.Empty);
	}

	private readonly IEqualityComparer<TLeft>? cmpLeft;
	private readonly IEqualityComparer<TRight>? cmpRight;
	private Snapshot snapshot;

	// ==========================================================================
	// ctors
	public FrozenSnapshotBijectiveMap(IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = Snapshot.Empty;
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	public FrozenSnapshotBijectiveMap(IEnumerable<(TLeft, TRight)> pairs, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = mksnap(pairs, cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	public FrozenSnapshotBijectiveMap(ReadOnlySpan<(TLeft, TRight)> pairs, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = mksnap(new PairSource(pairs), cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	public FrozenSnapshotBijectiveMap(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights, IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		snapshot = mksnap(new PairSource(lefts, rights), cmpLeft, cmpRight);
		this.cmpLeft = cmpLeft;
		this.cmpRight = cmpRight;
	}

	// ==========================================================================
	// private utility
	private static Snapshot freeze(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) => new(
		ltr.Count == 0 ? FrozenDictionary<TLeft, TRight>.Empty : ltr.ToFrozenDictionary(cmpLeft),
		rtl.Count == 0 ? FrozenDictionary<TRight, TLeft>.Empty : rtl.ToFrozenDictionary(cmpRight)
	);

	private static void add(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, TLeft left, TRight right) {
		if (ltr.ContainsKey(left))
			throw new ArgumentException("duplicate left key");
		if (rtl.ContainsKey(right))
			throw new ArgumentException("duplicate right key");
		ltr.Add(left, right);
		rtl.Add(right, left);
	}

	private static void setBijection(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, TLeft left, TRight right) {
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

	private static Snapshot mksnap(IEnumerable<(TLeft, TRight)> pairs, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		Dictionary<TLeft, TRight> ltr = new(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(cmpRight);
		foreach ((TLeft left, TRight right) in pairs)
			add(ltr, rtl, left, right);
		return freeze(ltr, rtl, cmpLeft, cmpRight);
	}

	private static Snapshot mksnap(PairSource pairs, IEqualityComparer<TLeft>? cmpLeft, IEqualityComparer<TRight>? cmpRight) {
		Dictionary<TLeft, TRight> ltr = new(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			add(ltr, rtl, left, right);
		}
		return freeze(ltr, rtl, cmpLeft, cmpRight);
	}

	// ==========================================================================
	// IReadOnlyBijectiveMap
	public int Count {
		get {
			Snapshot s = Volatile.Read(ref snapshot);
			return s.Ltr.Count;
		}
	}

	public IEnumerator<(TLeft Left, TRight Right)> GetEnumerator() {
		Snapshot s = Volatile.Read(ref snapshot);
		foreach (KeyValuePair<TLeft, TRight> kvp in s.Ltr)
			yield return (kvp.Key, kvp.Value);
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public bool ContainsLeft(TLeft left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Ltr.ContainsKey(left);
	}

	public bool ContainsRight(TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Rtl.ContainsKey(right);
	}

	public TRight GetByLeft(TLeft left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Ltr[left];
	}

	public TLeft GetByRight(TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Rtl[right];
	}

	public bool TryGetByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Ltr.TryGetValue(left, out right);
	}

	public bool TryGetByRight(TRight right, [NotNullWhen(true)] out TLeft? left) {
		Snapshot s = Volatile.Read(ref snapshot);
		return s.Rtl.TryGetValue(right, out left);
	}

	// ==========================================================================
	// IBijectiveMap
	public void Clear() => Volatile.Write(ref snapshot, Snapshot.Empty);

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

	public void Set(TLeft left, TRight right) {
		Snapshot s = Volatile.Read(ref snapshot);
		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		setBijection(ltr, rtl, left, right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public bool RemoveByLeft(TLeft left) => RemoveByLeft(left, out _);
	public bool RemoveByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (!s.Ltr.TryGetValue(left, out right))
			return false;
		Dictionary<TLeft, TRight> ltr = new(s.Ltr);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl);
		ltr.Remove(left);
		rtl.Remove(right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	public bool RemoveByRight(TRight right) => RemoveByRight(right, out _);
	public bool RemoveByRight(TRight right, [NotNullWhen(true)] out TLeft? left) {
		Snapshot s = Volatile.Read(ref snapshot);
		if (!s.Rtl.TryGetValue(right, out left))
			return false;
		Dictionary<TLeft, TRight> ltr = new(s.Ltr);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl);
		ltr.Remove(left);
		rtl.Remove(right);
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
		return true;
	}

	// ==========================================================================
	// bulk ops
	public void ReplaceContents(IEnumerable<(TLeft, TRight)> pairs) {
		Snapshot @new = mksnap(pairs, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

	public void ReplaceContents(ReadOnlySpan<(TLeft, TRight)> pairs) {
		Snapshot @new = mksnap(new PairSource(pairs), cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}

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
				throw new InvalidOperationException($"duplicate left key in the given list (index {i})");
			if (!seenRight.Add(right))
				throw new InvalidOperationException($"duplicate right key in the given list (index {i})");
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
	public void Add(ReadOnlySpan<(TLeft, TRight)> pairs) => add(new PairSource(pairs));
	public void Add(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) => add(new PairSource(lefts, rights));

	private void set(PairSource pairs) {
		Snapshot s = Volatile.Read(ref snapshot);
		Dictionary<TLeft, TRight> ltr = new(s.Ltr, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(s.Rtl, cmpRight);
		for (int i = 0; i < pairs.Length; i++) {
			(TLeft left, TRight right) = pairs.Get(i);
			setBijection(ltr, rtl, left, right);
		}
		Snapshot @new = freeze(ltr, rtl, cmpLeft, cmpRight);
		Volatile.Write(ref snapshot, @new);
	}
	public void Set(ReadOnlySpan<(TLeft, TRight)> pairs) => set(new PairSource(pairs));
	public void Set(ReadOnlySpan<TLeft> lefts, ReadOnlySpan<TRight> rights) => set(new PairSource(lefts, rights));

	// TODO: bulk-remove
}
