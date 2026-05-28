// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Injure.DataStructures;

/// <summary>
/// Represents a value paired with two associated lookup keys.
/// </summary>
public readonly struct DualKeyValuePair<TKeyL, TKeyR, TValue>(TKeyL left, TKeyR right, TValue value) where TKeyL : notnull where TKeyR : notnull {
	public TKeyL LeftKey { get; } = left;
	public TKeyR RightKey { get; } = right;
	public TValue Value { get; } = value;
}

/// <summary>
/// A <see cref="Dictionary{TKey, TValue}"/>-like collection where each value has two keys (left and right)
/// and may be looked up by either.
/// </summary>
/// <typeparam name="TKeyL">The left key type.</typeparam>
/// <typeparam name="TKeyR">The right key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed class DualKeyDictionary<TKeyL, TKeyR, TValue> : IEnumerable<DualKeyValuePair<TKeyL, TKeyR, TValue>> where TKeyL : notnull where TKeyR : notnull {
	private sealed class Entry(TKeyL l, TKeyR r, TValue v) {
		public TKeyL LKey = l;
		public TKeyR RKey = r;
		public TValue Value = v;
		public DualKeyValuePair<TKeyL, TKeyR, TValue> ToPair() => new(LKey, RKey, Value);
	}

	private readonly Dictionary<TKeyL, Entry> lDict;
	private readonly Dictionary<TKeyR, Entry> rDict;

	/// <summary>
	/// Initializes an empty instance using the default equality comparers.
	/// </summary>
	public DualKeyDictionary() : this(0, null, null) {
	}

	/// <summary>
	/// Initializes an empty instance using the specified initial capacity and default equality comparers.
	/// </summary>
	/// <param name="capacity">The initial capacity.</param>
	public DualKeyDictionary(int capacity) : this(capacity, null, null) {
	}

	/// <summary>
	/// Initializes an empty instance using the specified equality comparers.
	/// </summary>
	/// <param name="leftComparer">Equality comparer for left keys, or <see langword="null"/> to use the default comparer.</param>
	/// <param name="rightComparer">Equality comparer for right keys, or <see langword="null"/> to use the default comparer.</param>
	public DualKeyDictionary(IEqualityComparer<TKeyL>? leftComparer, IEqualityComparer<TKeyR>? rightComparer) : this(0, leftComparer, rightComparer) {
	}

#pragma warning disable IDE0290 // use primary constructor
	/// <summary>
	/// Initializes an empty instance using the specified initial capacity and equality comparers.
	/// </summary>
	/// <param name="capacity">The initial capacity.</param>
	/// <param name="leftComparer">Equality comparer for left keys, or <see langword="null"/> to use the default comparer.</param>
	/// <param name="rightComparer">Equality comparer for right keys, or <see langword="null"/> to use the default comparer.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="capacity"/> is negative.
	/// </exception>
	public DualKeyDictionary(int capacity, IEqualityComparer<TKeyL>? leftComparer, IEqualityComparer<TKeyR>? rightComparer) {
		lDict = new Dictionary<TKeyL, Entry>(capacity, leftComparer);
		rDict = new Dictionary<TKeyR, Entry>(capacity, rightComparer);
	}
#pragma warning restore IDE0290 // use primary constructor

	/// <summary>
	/// Initializes an instance from an existing sequence of dual-key+value pairs.
	/// </summary>
	/// <param name="items">The items to add.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="items"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the sequence contains a duplicate left key or duplicate right key.
	/// </exception>
	public DualKeyDictionary(IEnumerable<DualKeyValuePair<TKeyL, TKeyR, TValue>> items) : this(items, null, null) {
	}

	/// <summary>
	/// Initializes an instance from an existing sequence of dual-key value pairs using the specified equality comparers.
	/// </summary>
	/// <param name="items">The items to add.</param>
	/// <param name="leftComparer">Equality comparer for left keys, or <see langword="null"/> to use the default comparer.</param>
	/// <param name="rightComparer">Equality comparer for right keys, or <see langword="null"/> to use the default comparer.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="items"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the sequence contains a duplicate left key or duplicate right key.
	/// </exception>
	public DualKeyDictionary(IEnumerable<DualKeyValuePair<TKeyL, TKeyR, TValue>> items, IEqualityComparer<TKeyL>? leftComparer, IEqualityComparer<TKeyR>? rightComparer) : this(0, leftComparer, rightComparer) {
		ArgumentNullException.ThrowIfNull(items);
		foreach (DualKeyValuePair<TKeyL, TKeyR, TValue> dkvp in items)
			Add(dkvp.LeftKey, dkvp.RightKey, dkvp.Value);
	}

	/// <summary>
	/// The number of values in the dictionary.
	/// </summary>
	public int Count => lDict.Count;

	/// <summary>
	/// The equality comparer used for left keys.
	/// </summary>
	public IEqualityComparer<TKeyL> LeftComparer => lDict.Comparer;

	/// <summary>
	/// The equality comparer used for right keys.
	/// </summary>
	public IEqualityComparer<TKeyR> RightComparer => rDict.Comparer;

	/// <summary>
	/// The left keys.
	/// </summary>
	public IEnumerable<TKeyL> LeftKeys => lDict.Keys;

	/// <summary>
	/// The right keys.
	/// </summary>
	public IEnumerable<TKeyR> RightKeys => rDict.Keys;

	/// <summary>
	/// The values.
	/// </summary>
	public IEnumerable<TValue> Values => lDict.Values.Select(static e => e.Value);

	/// <summary>
	/// Adds the specified value and its left/right keys.
	/// </summary>
	/// <param name="left">The left key.</param>
	/// <param name="right">The right key.</param>
	/// <param name="value">The value.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="left"/> or <paramref name="right"/> is already in the dictionary.
	/// </exception>
	public void Add(TKeyL left, TKeyR right, TValue value) {
		if (lDict.ContainsKey(left))
			throw new ArgumentException("the left key is already in the dictionary", nameof(left));
		if (rDict.ContainsKey(right))
			throw new ArgumentException("the right key is already in the dictionary", nameof(right));
		Entry ent = new(left, right, value);
		lDict.Add(left, ent);
		rDict.Add(right, ent);
	}

	/// <summary>
	/// Attempts to add the specified value and its left/right keys.
	/// </summary>
	/// <param name="left">The left key.</param>
	/// <param name="right">The right key.</param>
	/// <param name="value">The value.</param>
	/// <returns>
	/// <see langword="true"/> if the value was added; <see langword="false"/> if either key is
	/// already in the dictionary.
	/// </returns>
	public bool TryAdd(TKeyL left, TKeyR right, TValue value) {
		if (lDict.ContainsKey(left) || rDict.ContainsKey(right))
			return false;
		Entry ent = new(left, right, value);
		lDict.Add(left, ent);
		rDict.Add(right, ent);
		return true;
	}

	/// <summary>
	/// Adds a new value or updates the existing value for the exact same left/right key pair.
	/// </summary>
	/// <param name="left">The left key.</param>
	/// <param name="right">The right key.</param>
	/// <param name="value">The new value.</param>
	/// <exception cref="ArgumentException">
	/// One key exists, but it is associated with a different opposite key.
	/// </exception>
	public void AddOrSet(TKeyL left, TKeyR right, TValue value) {
		bool hasL = lDict.TryGetValue(left, out Entry? leftEntry);
		bool hasR = rDict.TryGetValue(right, out Entry? rightEntry);
		if (!hasL && !hasR) {
			Add(left, right, value);
			return;
		}
		if (hasL && hasR) {
			if (leftEntry is null || rightEntry is null)
				throw new InternalStateException("expected Dictionary.TryGetValue success to yield nonnull value");
			if (ReferenceEquals(leftEntry, rightEntry)) {
				leftEntry.Value = value;
				return;
			}
		}
		throw new ArgumentException("the left/right keys do not identify the same existing value");
	}

	/// <summary>
	/// Removes all keys and values from the dictionary.
	/// </summary>
	public void Clear() {
		lDict.Clear();
		rDict.Clear();
	}

	/// <summary>
	/// Determines whether the specified left key in the dictionary.
	/// </summary>
	public bool ContainsLeft(TKeyL left) => lDict.ContainsKey(left);

	/// <summary>
	/// Determines whether the specified right key exists.
	/// </summary>
	public bool ContainsRight(TKeyR right) => rDict.ContainsKey(right);

	/// <summary>
	/// Determines whether the specified exact left/right key pair exists in the dictionary.
	/// </summary>
	public bool ContainsPair(TKeyL left, TKeyR right) =>
		lDict.TryGetValue(left, out Entry? lEnt) && rDict.TryGetValue(right, out Entry? rEnt) && ReferenceEquals(lEnt, rEnt);

	/// <summary>
	/// Gets the value associated with the specified left key.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if the left key is not in the dictionary.
	/// </exception>
	public TValue GetByLeft(TKeyL left) => lDict[left].Value;

	/// <summary>
	/// Gets the value associated with the specified right key.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if the right key is not in the dictionary.
	/// </exception>
	public TValue GetByRight(TKeyR right) => rDict[right].Value;

	/// <summary>
	/// Attempts to get the value associated with the specified left key.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the left key was found in the dictionary; otherwise, <see langword="false"/>.
	/// </returns>
	public bool TryGetByLeft(TKeyL left, [MaybeNullWhen(false)] out TValue value) {
		if (lDict.TryGetValue(left, out Entry? ent)) {
			value = ent.Value;
			return true;
		}
		value = default;
		return false;
	}

	/// <summary>
	/// Attempts to get the value associated with the specified right key.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the right key was found in the dictionary; otherwise, <see langword="false"/>.
	/// </returns>
	public bool TryGetByRight(TKeyR right, [MaybeNullWhen(false)] out TValue value) {
		if (rDict.TryGetValue(right, out Entry? ent)) {
			value = ent.Value;
			return true;
		}
		value = default;
		return false;
	}

	/// <summary>
	/// Gets the full pair associated with the specified left key.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if the left key is not in the dictionary.
	/// </exception>
	public DualKeyValuePair<TKeyL, TKeyR, TValue> GetPairByLeft(TKeyL left) => lDict[left].ToPair();

	/// <summary>
	/// Gets the full pair associated with the specified right key.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if the right key is not in the dictionary.
	/// </exception>
	public DualKeyValuePair<TKeyL, TKeyR, TValue> GetPairByRight(TKeyR right) => rDict[right].ToPair();

	/// <summary>
	/// Attempts to get the full pair associated with the specified left key.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the left key was found in the dictionary; otherwise, <see langword="false"/>.
	/// </returns>
	public bool TryGetPairByLeft(TKeyL left, out DualKeyValuePair<TKeyL, TKeyR, TValue> pair) {
		if (lDict.TryGetValue(left, out Entry? ent)) {
			pair = ent.ToPair();
			return true;
		}
		pair = default;
		return false;
	}

	/// <summary>
	/// Attempts to get the full pair associated with the specified right key.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the right key was found in the dictionary; otherwise, <see langword="false"/>.
	/// </returns>
	public bool TryGetByRight(TKeyR right, out DualKeyValuePair<TKeyL, TKeyR, TValue> pair) {
		if (rDict.TryGetValue(right, out Entry? ent)) {
			pair = ent.ToPair();
			return true;
		}
		pair = default;
		return false;
	}

	/// <summary>
	/// Sets the value associated with the specified left key.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if the left key is not in the dictionary.
	/// </exception>
	public void SetByLeft(TKeyL left, TValue value) => lDict[left].Value = value;

	/// <summary>
	/// Sets the value associated with the specified right key.
	/// </summary>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if the right key is not in the dictionary.
	/// </exception>
	public void SetByRight(TKeyR right, TValue value) => rDict[right].Value = value;

	/// <summary>
	/// Removes the value associated with the specified left key.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the left key was found in the dictionary, and as such the
	/// associated item was removed; otherwise, <see langword="false"/>.
	/// </returns>
	public bool RemoveByLeft(TKeyL left, out DualKeyValuePair<TKeyL, TKeyR, TValue> pair) {
		if (!lDict.Remove(left, out Entry? ent)) {
			pair = default;
			return false;
		}
		rDict.Remove(ent.RKey);
		pair = ent.ToPair();
		return true;
	}

	/// <summary>
	/// Removes the value associated with the specified right key.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the right key was found in the dictionary, and as such the
	/// associated item was removed; otherwise, <see langword="false"/>.
	/// </returns>
	public bool RemoveByRight(TKeyR right, out DualKeyValuePair<TKeyL, TKeyR, TValue> pair) {
		if (!rDict.Remove(right, out Entry? ent)) {
			pair = default;
			return false;
		}
		lDict.Remove(ent.LKey);
		pair = ent.ToPair();
		return true;
	}

	/// <summary>
	/// Replaces the left key for an existing value.
	/// </summary>
	/// <param name="oldLeft">The existing left key.</param>
	/// <param name="newLeft">The new left key.</param>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if <paramref name="oldLeft"/> is not in the dictionary.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="newLeft"/> is already in the dictionary.
	/// </exception>
	public void ReplaceLeftKey(TKeyL oldLeft, TKeyL newLeft) {
		if (lDict.Comparer.Equals(oldLeft, newLeft))
			return;
		if (!lDict.TryGetValue(oldLeft, out Entry? ent))
			throw new KeyNotFoundException("the old left key is not in the dictionary");
		if (lDict.ContainsKey(newLeft))
			throw new ArgumentException("the new left key is already in the dictionary", nameof(newLeft));
		lDict.Remove(oldLeft);
		ent.LKey = newLeft;
		lDict.Add(newLeft, ent);
	}

	/// <summary>
	/// Replaces the right key for an existing value.
	/// </summary>
	/// <param name="oldRight">The existing left key.</param>
	/// <param name="newRight">The new left key.</param>
	/// <exception cref="KeyNotFoundException">
	/// Thrown if <paramref name="oldRight"/> is not in the dictionary.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="newRight"/> is already in the dictionary.
	/// </exception>
	public void ReplaceRightKey(TKeyR oldRight, TKeyR newRight) {
		if (rDict.Comparer.Equals(oldRight, newRight))
			return;
		if (!rDict.TryGetValue(oldRight, out Entry? ent))
			throw new KeyNotFoundException("the old right key is not in the dictionary");
		if (rDict.ContainsKey(newRight))
			throw new ArgumentException("the new right key is already in the dictionary", nameof(newRight));
		rDict.Remove(oldRight);
		ent.RKey = newRight;
		rDict.Add(newRight, ent);
	}

	/// <summary>
	/// Returns an enumerator that iterates over every pair in the dictionary.
	/// </summary>
	public IEnumerator<DualKeyValuePair<TKeyL, TKeyR, TValue>> GetEnumerator() => lDict.Values.Select(static e => e.ToPair()).GetEnumerator();
	/// <inheritdoc cref="GetEnumerator()"/>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
