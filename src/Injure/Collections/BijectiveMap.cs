// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Collections;

/// <summary>
/// A mutable <typeparamref name="TLeft"/> &lt;-&gt; <typeparamref name="TRight"/> bijection.
/// </summary>
/// <typeparam name="TLeft">Type of the left side of a pair.</typeparam>
/// <typeparam name="TRight">Type of the right side of a pair.</typeparam>
/// <remarks>
/// <para>
/// <b>Not thread-safe/synchronized.</b> Concurrent mutating operations must be externally mutexed,
/// including with reads.
/// </para>
/// <para>
/// Lookup, insertion, and removal are all O(1).
/// </para>
/// </remarks>
public sealed class BijectiveMap<TLeft, TRight>
	: IReadOnlyBijectiveMap<TLeft, TRight> where TLeft : notnull where TRight : notnull {
	// ==========================================================================
	// bookkeeping
	private readonly Dictionary<TLeft, TRight> ltr;
	private readonly Dictionary<TRight, TLeft> rtl;

	// ==========================================================================
	// ctors

	/// <summary>
	/// Creates an empty map.
	/// </summary>
	public BijectiveMap(IEqualityComparer<TLeft>? cmpLeft = null, IEqualityComparer<TRight>? cmpRight = null) {
		ltr = new Dictionary<TLeft, TRight>(cmpLeft);
		rtl = new Dictionary<TRight, TLeft>(cmpRight);
	}

	/// <summary>
	/// Creates a map with <paramref name="pairs"/> as the contents.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if multiple pairs share a left value or share a right value.
	/// </exception>
	public BijectiveMap(
		IEnumerable<(TLeft Left, TRight Right)> pairs,
		IEqualityComparer<TLeft>? cmpLeft = null,
		IEqualityComparer<TRight>? cmpRight = null
	) {
		(ltr, rtl) = make(pairs, cmpLeft, cmpRight);
	}

	/// <summary>
	/// Creates a map with <paramref name="lefts"/> and <paramref name="rights"/> zipped as the contents.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="lefts"/> and <paramref name="rights"/> differ in length, or if multiple
	/// pairs share a left value or share a right value.
	/// </exception>
	public BijectiveMap(
		TLeft[] lefts,
		TRight[] rights,
		IEqualityComparer<TLeft>? cmpLeft = null,
		IEqualityComparer<TRight>? cmpRight = null
	) {
		(ltr, rtl) = make(lefts, rights, cmpLeft, cmpRight);
	}

	// ==========================================================================
	// private utility
	private static void add(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, TLeft left, TRight right) {
		if (ltr.ContainsKey(left))
			throw new ArgumentException("duplicate left key");
		if (rtl.ContainsKey(right))
			throw new ArgumentException("duplicate right key");
		ltr.Add(left, right);
		rtl.Add(right, left);
	}

	private static void set(Dictionary<TLeft, TRight> ltr, Dictionary<TRight, TLeft> rtl, TLeft left, TRight right) {
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

	private static (Dictionary<TLeft, TRight> Ltr, Dictionary<TRight, TLeft> Rtl) make(
		IEnumerable<(TLeft Left, TRight Right)> pairs,
		IEqualityComparer<TLeft>? cmpLeft,
		IEqualityComparer<TRight>? cmpRight
	) {
		Dictionary<TLeft, TRight> ltr = new(cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(cmpRight);
		foreach ((TLeft left, TRight right) in pairs)
			add(ltr, rtl, left, right);
		return (ltr, rtl);
	}

	private static (Dictionary<TLeft, TRight> Ltr, Dictionary<TRight, TLeft> Rtl) make(
		TLeft[] lefts,
		TRight[] rights,
		IEqualityComparer<TLeft>? cmpLeft,
		IEqualityComparer<TRight>? cmpRight
	) {
		if (lefts.Length != rights.Length)
			throw new ArgumentException("passed left<->right map arrays must be of equal length");
		Dictionary<TLeft, TRight> ltr = new(lefts.Length, cmpLeft);
		Dictionary<TRight, TLeft> rtl = new(rights.Length, cmpRight);
		for (int i = 0; i < lefts.Length; i++)
			add(ltr, rtl, lefts[i], rights[i]);
		return (ltr, rtl);
	}

	// ==========================================================================
	// IReadOnlyBijectiveMap

	/// <summary>
	/// Number of pairs in the map.
	/// </summary>
	public int Count => ltr.Count;

	/// <summary>
	/// Enumerates the pairs in unspecified order.
	/// </summary>
	/// <remarks>
	/// Mutating the map invalidates every live enumerator over it.
	/// </remarks>
	public IEnumerator<(TLeft Left, TRight Right)> GetEnumerator() {
		foreach (KeyValuePair<TLeft, TRight> kvp in ltr)
			yield return (kvp.Key, kvp.Value);
	}
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <inheritdoc/>
	public bool ContainsLeft(TLeft left) => ltr.ContainsKey(left);
	/// <inheritdoc/>
	public bool ContainsRight(TRight right) => rtl.ContainsKey(right);
	/// <inheritdoc/>
	public bool TryGetByLeft(TLeft left, [NotNullWhen(true)] out TRight? right) => ltr.TryGetValue(left, out right);
	/// <inheritdoc/>
	public bool TryGetByRight(TRight right, [NotNullWhen(true)] out TLeft? left) => rtl.TryGetValue(right, out left);
	/// <inheritdoc/>
	public TRight GetByLeft(TLeft left) => ltr[left];
	/// <inheritdoc/>
	public TLeft GetByRight(TRight right) => rtl[right];

	// ==========================================================================
	// mutation

	/// <summary>
	/// Removes every pair.
	/// </summary>
	public void Clear() {
		ltr.Clear();
		rtl.Clear();
	}

	/// <summary>
	/// Adds the pair (<paramref name="left"/> &lt;-&gt; <paramref name="right"/>).
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if either value already belongs to a pair. The map is left unchanged.
	/// </exception>
	public void Add(TLeft left, TRight right) => add(ltr, rtl, left, right);

	/// <summary>
	/// Adds the pair (<paramref name="left"/> &lt;-&gt; <paramref name="right"/>) unless either value
	/// already belongs to a pair.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the pair was added.
	/// </returns>
	public bool TryAdd(TLeft left, TRight right) {
		if (ltr.ContainsKey(left) || rtl.ContainsKey(right))
			return false;
		set(ltr, rtl, left, right);
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
	public void Set(TLeft left, TRight right) => set(ltr, rtl, left, right);

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
		if (!ltr.TryGetValue(left, out right))
			return false;
		ltr.Remove(left);
		rtl.Remove(right);
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
		if (!rtl.TryGetValue(right, out left))
			return false;
		rtl.Remove(right);
		ltr.Remove(left);
		return true;
	}
}
