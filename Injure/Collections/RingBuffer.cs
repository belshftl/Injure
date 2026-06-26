// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Injure.Collections;

/// <summary>
/// Fixed-capacity deque-like ring buffer with a logical oldest-to-newest order.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// <para>
/// <b>Not thread-safe/synchronized.</b> Concurrent mutating operations must be externally mutexed.
/// </para>
/// <para>
/// Index <c>0</c> refers to the oldest element currently stored in the buffer, and index
/// <c><see cref="Count"/> - 1</c> refers to the newest.
/// </para>
/// <para>
/// The buffer has a fixed <see cref="Capacity"/>. Methods such as <see cref="PushNewest(T)"/>
/// and <see cref="PushOldest(T)"/> overwrite an existing element when the buffer is full.
/// The corresponding <c>TryPush*</c> methods do not overwrite and instead return <see langword="false"/>.
/// </para>
/// <para>
/// Standard enumeration goes from oldest to newest. Reverse enumeration is supported.
/// Enumeration through the concrete <see cref="RingBuffer{T}"/> or <see cref="ReverseEnumerable"/> types
/// uses a value-type enumerator and performs no managed heap allocations. Enumeration through interfaces
/// performs standard struct-to-interface boxing.
/// </para>
/// <para>
/// All singular enumerator operations such as construction, <c>Current</c>, <c>MoveNext</c>, <c>Reset</c>,
/// and <c>Dispose</c> are O(1). Complete enumeration in either direction is O(n) and non-allocating, not
/// counting struct-to-interface boxing or exception paths allocating exceptions. Reverse enumeration begins
/// directly at the newest physical element and does not scan for it.
/// </para>
/// </remarks>
[DebuggerDisplay("Count = {Count}, Capacity = {Capacity}, Head = {head}")]
[DebuggerTypeProxy(typeof(RingBufferDebuggerTypeProxy<>))]
public sealed class RingBuffer<T> : IReadOnlyList<T> {
	private readonly T[] buf;
	private int head;
	private int count;
	private ulong version = 0;

	internal ulong Version => version;

	/// <summary>
	/// Initializes a new ring buffer with the specified fixed capacity.
	/// </summary>
	/// <param name="capacity">The maximum number of elements the buffer can hold.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="capacity"/> is less than or equal to zero.
	/// </exception>
	public RingBuffer(int capacity) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		buf = new T[capacity];
	}

	/// <summary>
	/// Initializes a new ring buffer with the specified fixed capacity and appends the provided items.
	/// </summary>
	/// <param name="capacity">The maximum number of elements the buffer can hold.</param>
	/// <param name="items">The items to append in enumeration order.</param>
	/// <remarks>
	/// If <paramref name="items"/> contains more than <paramref name="capacity"/> elements,
	/// the oldest excess elements are discarded according to normal overwrite semantics.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="capacity"/> is less than or equal to zero.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="items"/> is <see langword="null"/>.
	/// </exception>
	public RingBuffer(int capacity, IEnumerable<T> items) : this(capacity) {
		ArgumentNullException.ThrowIfNull(items);
		foreach (T item in items)
			PushNewest(item);
	}

	// ==========================================================================
	// private helpers
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void throwIfEmpty() {
		if (count == 0)
			throw new InvalidOperationException("ring buffer is empty");
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int physicalIndex(int logicalIndex) {
		int idx = head + logicalIndex;
		return idx < buf.Length ? idx : idx - buf.Length;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int incr(int idx) => idx + 1 == buf.Length ? 0 : idx + 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int decr(int idx) => idx == 0 ? buf.Length - 1 : idx - 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void clearSlot(int idx) {
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
			buf[idx] = default!;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void validateSlice(int start, int length) {
		if ((uint)start > (uint)count)
			throw new ArgumentOutOfRangeException(nameof(start));
		if ((uint)length > (uint)(count - start))
			throw new ArgumentOutOfRangeException(nameof(length));
	}

	// ==========================================================================
	// public api

	/// <summary>
	/// The maximum number of elements the buffer can hold.
	/// </summary>
	public int Capacity => buf.Length;

	/// <summary>
	/// How many more elements the buffer can hold.
	/// </summary>
	public int RemainingCapacity => buf.Length - count;

	/// <summary>
	/// The number of elements currently stored in the buffer.
	/// </summary>
	public int Count => count;

	/// <summary>
	/// Whether the buffer currently contains no elements.
	/// </summary>
	public bool IsEmpty => count == 0;

	/// <summary>
	/// Whether the buffer is currently at full capacity.
	/// </summary>
	public bool IsFull => count == buf.Length;

	/// <summary>
	/// Gets the element at the specified logical index.
	/// </summary>
	/// <param name="idx">
	/// The logical index, where <c>0</c> is the oldest element.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="idx"/> is outside the bounds of the buffer.
	/// </exception>
	public T this[int idx] {
		get {
			if ((uint)idx >= (uint)count)
				throw new ArgumentOutOfRangeException(nameof(idx));
			return buf[physicalIndex(idx)];
		}
	}

	/// <summary>
	/// Returns the oldest element in the buffer without removing it.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is empty.
	/// </exception>
	public T PeekOldest() {
		throwIfEmpty();
		return buf[head];
	}

	/// <summary>
	/// Returns the newest element in the buffer without removing it.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is empty.
	/// </exception>
	public T PeekNewest() {
		throwIfEmpty();
		return buf[physicalIndex(count - 1)];
	}

	/// <summary>
	/// Attempts to return the oldest element in the buffer without removing it.
	/// </summary>
	/// <param name="item">
	/// If the method returned <see langword="true"/>, the oldest element; otherwise,
	/// the default value of <typeparamref name="T"/>.
	/// </param>
	/// <returns><see langword="true"/> if the buffer is non-empty; otherwise, <see langword="false"/>.</returns>
	public bool TryPeekOldest([MaybeNullWhen(false)] out T item) {
		if (count == 0) {
			item = default;
			return false;
		}
		item = buf[head];
		return true;
	}

	/// <summary>
	/// Attempts to return the newest element in the buffer without removing it.
	/// </summary>
	/// <param name="item">
	/// If the method returned <see langword="true"/>, the newest element; otherwise,
	/// the default value of <typeparamref name="T"/>.
	/// </param>
	/// <returns><see langword="true"/> if the buffer is non-empty; otherwise, <see langword="false"/>.</returns>
	public bool TryPeekNewest([MaybeNullWhen(false)] out T item) {
		if (count == 0) {
			item = default;
			return false;
		}
		item = buf[physicalIndex(count - 1)];
		return true;
	}

	/// <summary>
	/// Appends an element at the logical end of the buffer.
	/// </summary>
	/// <param name="item">The element to append.</param>
	/// <remarks>
	/// If the buffer is full, the oldest element is overwritten.
	/// </remarks>
	public void PushNewest(T item) {
		if (count < buf.Length) {
			buf[physicalIndex(count)] = item;
			count++;
		} else {
			buf[head] = item;
			head = incr(head);
		}
		version++;
	}

	/// <summary>
	/// Prepends an element at the logical beginning of the buffer.
	/// </summary>
	/// <param name="item">The element to prepend.</param>
	/// <remarks>
	/// If the buffer is full, the newest element is overwritten.
	/// </remarks>
	public void PushOldest(T item) {
		head = decr(head);
		buf[head] = item;
		if (count < buf.Length)
			count++;
		version++;
	}

	/// <summary>
	/// Attempts to append an element at the logical end of the buffer without overwriting/removing
	/// any existing elements.
	/// </summary>
	/// <param name="item">The element to append.</param>
	/// <returns>
	/// <see langword="true"/> if the element was appended; <see langword="false"/> if the buffer was full.
	/// </returns>
	public bool TryPushNewest(T item) {
		if (count == buf.Length)
			return false;
		buf[physicalIndex(count)] = item;
		count++;
		version++;
		return true;
	}

	/// <summary>
	/// Attempts to prepend an element at the logical beginning of the buffer without overwriting/removing
	/// any existing elements.
	/// </summary>
	/// <param name="item">The element to prepend.</param>
	/// <returns>
	/// <see langword="true"/> if the element was prepended; <see langword="false"/> if the buffer was full.
	/// </returns>
	public bool TryPushOldest(T item) {
		if (count == buf.Length)
			return false;
		head = decr(head);
		buf[head] = item;
		count++;
		version++;
		return true;
	}

	/// <summary>
	/// Removes and returns the oldest element in the buffer.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is empty.
	/// </exception>
	public T PopOldest() {
		throwIfEmpty();
		T item = buf[head];
		clearSlot(head);
		head = incr(head);
		if (--count == 0)
			head = 0;
		version++;
		return item;
	}

	/// <summary>
	/// Removes and returns the newest element in the buffer.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is empty.
	/// </exception>
	public T PopNewest() {
		throwIfEmpty();
		int index = physicalIndex(count - 1);
		T item = buf[index];
		clearSlot(index);
		if (--count == 0)
			head = 0;
		version++;
		return item;
	}

	/// <summary>
	/// Attempts to remove and return the oldest element in the buffer.
	/// </summary>
	/// <param name="item">
	/// If the method returned <see langword="true"/>, the removed element; otherwise,
	/// the default value of <typeparamref name="T"/>.
	/// </param>
	/// <returns><see langword="true"/> if the buffer is non-empty; otherwise, <see langword="false"/>.</returns>
	public bool TryPopOldest([MaybeNullWhen(false)] out T item) {
		if (count == 0) {
			item = default;
			return false;
		}
		item = buf[head];
		clearSlot(head);
		head = incr(head);
		if (--count == 0)
			head = 0;
		version++;
		return true;
	}

	/// <summary>
	/// Attempts to remove and return the newest element in the buffer.
	/// </summary>
	/// <param name="item">
	/// If the method returned <see langword="true"/>, the removed element; otherwise,
	/// the default value of <typeparamref name="T"/>.
	/// </param>
	/// <returns><see langword="true"/> if the buffer is non-empty; otherwise, <see langword="false"/>.</returns>
	public bool TryPopNewest([MaybeNullWhen(false)] out T item) {
		if (count == 0) {
			item = default;
			return false;
		}
		int index = physicalIndex(count - 1);
		item = buf[index];
		clearSlot(index);
		if (--count == 0)
			head = 0;
		version++;
		return true;
	}

	/// <summary>
	/// Removes all elements from the buffer.
	/// </summary>
	/// <remarks>
	/// The capacity of the buffer is unchanged.
	/// </remarks>
	public void Clear() {
		if (count != 0 && RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
			int firstPart = Math.Min(count, buf.Length - head);
			Array.Clear(buf, head, firstPart);
			Array.Clear(buf, 0, count - firstPart);
		}
		head = 0;
		count = 0;
		version++;
	}

	/// <summary>
	/// Determines whether the buffer contains the specified element, using
	/// the specified equality comparer for the type.
	/// </summary>
	public bool Contains(T item, IEqualityComparer<T> comparer) => IndexOf(item, comparer) >= 0;

	/// <summary>
	/// Determines whether the buffer contains the specified element, using
	/// the default equality comparer for the type.
	/// </summary>
	public bool Contains(T item) => IndexOf(item) >= 0;

	/// <summary>
	/// Returns the logical index of the first occurrence of the specified element,
	/// using the specified equality comparer for the type.
	/// </summary>
	public int IndexOf(T item, IEqualityComparer<T> comparer) {
		ArgumentNullException.ThrowIfNull(comparer);
		int firstPart = Math.Min(count, buf.Length - head);
		for (int i = 0; i < firstPart; i++)
			if (comparer.Equals(buf[head + i], item))
				return i;
		for (int i = 0; i < count - firstPart; i++)
			if (comparer.Equals(buf[i], item))
				return firstPart + i;
		return -1;
	}

	/// <summary>
	/// Returns the logical index of the first occurrence of the specified element,
	/// using the default equality comparer for the type.
	/// </summary>
	public int IndexOf(T item) => IndexOf(item, EqualityComparer<T>.Default);

	/// <summary>
	/// Copies the contents of the buffer into the specified span.
	/// </summary>
	/// <param name="dst">The destination span.</param>
	/// <remarks>
	/// Elements are copied in the buffer's enumeration order (oldest to newest).
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="dst"/> is too small to be able to hold
	/// <see cref="Count"/> elements.
	/// </exception>
	public void CopyTo(Span<T> dst) {
		if (dst.Length < count)
			throw new ArgumentException($"destination span is too small (length = {dst.Length}, expected at least {count})", nameof(dst));
		int firstPart = Math.Min(count, buf.Length - head);
		buf.AsSpan(head, firstPart).CopyTo(dst);
		if (firstPart < count)
			buf.AsSpan(0, count - firstPart).CopyTo(dst[firstPart..]);
	}

	/// <summary>
	/// Returns a new array containing the contents of the buffer.
	/// </summary>
	/// <remarks>
	/// Elements are in the buffer's enumeration order (oldest to newest).
	/// </remarks>
	public T[] ToArray() {
		if (count == 0)
			return Array.Empty<T>();
		var arr = new T[count];
		CopyTo(arr);
		return arr;
	}

	/// <summary>
	/// Returns up to two contiguous spans that together represent a slice of the current
	/// logical contents.
	/// </summary>
	/// <param name="start">
	/// Start of the slice.
	/// </param>
	/// <param name="length">
	/// Length of the slice.
	/// </param>
	/// <param name="first">
	/// The first contiguous span of elements.
	/// </param>
	/// <param name="second">
	/// If the contents wrap around the end of the backing array, the second contiguous span
	/// of elements; otherwise, an empty span.
	/// </param>
	/// <remarks>
	/// <para>
	/// The returned spans are views into the live backing storage. Any subsequent mutation
	/// of the buffer may change the values visible through them or cause them to no longer
	/// represent the current logical contents.
	/// </para>
	/// <para>
	/// See <see cref="GetMemory(int, int, out ReadOnlyMemory{T}, out ReadOnlyMemory{T})"/> for a similar
	/// method that returns <see cref="ReadOnlyMemory{T}"/> regions instead.
	/// </para>
	/// </remarks>
	public void GetSegments(int start, int length, out ReadOnlySpan<T> first, out ReadOnlySpan<T> second) {
		validateSlice(start, length);
		if (count == 0 || length == 0) {
			first = second = ReadOnlySpan<T>.Empty;
			return;
		}
		int physicalStart = physicalIndex(start);
		int firstLength = Math.Min(length, buf.Length - physicalStart);
		first = buf.AsSpan(physicalStart, firstLength);
		second = buf.AsSpan(0, length - firstLength);
	}

	/// <summary>
	/// Returns up to two contiguous spans that together represent the current logical contents.
	/// </summary>
	/// <inheritdoc cref="GetSegments(int, int, out ReadOnlySpan{T}, out ReadOnlySpan{T})"/>
	public void GetSegments(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second) =>
		GetSegments(0, count, out first, out second);

	/// <summary>
	/// Returns up to two contiguous memory regions that together represent a slice of the
	/// current logical contents.
	/// </summary>
	/// <param name="start">
	/// Start of the slice.
	/// </param>
	/// <param name="length">
	/// Length of the slice.
	/// </param>
	/// <param name="first">
	/// The first contiguous region of elements.
	/// </param>
	/// <param name="second">
	/// If the contents wrap around the end of the backing array, the second contiguous region
	/// of elements; otherwise, an empty region.
	/// </param>
	/// <remarks>
	/// <para>
	/// The returned regions are views into the live backing storage. Any subsequent mutation
	/// of the buffer may change the values visible through them or cause them to no longer
	/// represent the current logical contents.
	/// </para>
	/// <para>
	/// See <see cref="GetSegments(int, int, out ReadOnlySpan{T}, out ReadOnlySpan{T})"/> for a similar
	/// method that returns spans instead.
	/// </para>
	/// </remarks>
	public void GetMemory(int start, int length, out ReadOnlyMemory<T> first, out ReadOnlyMemory<T> second) {
		validateSlice(start, length);
		if (count == 0 || length == 0) {
			first = second = ReadOnlyMemory<T>.Empty;
			return;
		}
		int physicalStart = physicalIndex(start);
		int firstLength = Math.Min(length, buf.Length - physicalStart);
		first = new ReadOnlyMemory<T>(buf, physicalStart, firstLength);
		second = new ReadOnlyMemory<T>(buf, 0, length - firstLength);
	}

	/// <summary>
	/// Returns up to two contiguous memory regions that together represent the current logical contents.
	/// </summary>
	/// <inheritdoc cref="GetMemory(int, int, out ReadOnlyMemory{T}, out ReadOnlyMemory{T})"/>
	public void GetMemory(out ReadOnlyMemory<T> first, out ReadOnlyMemory<T> second) =>
		GetMemory(0, count, out first, out second);

	/// <summary>
	/// Returns a non-copying, read-only <see cref="RingBufferView{T}"/> over the current logical contents.
	/// </summary>
	public RingBufferView<T> View() => View(0, count);

	/// <summary>
	/// Returns a non-copying, read-only <see cref="RingBufferView{T}"/> over a slice of the current
	/// logical contents.
	/// </summary>
	/// <param name="range">
	/// The slice of the buffer to capture.
	/// </param>
	public RingBufferView<T> View(Range range) {
		(int start, int length) = range.GetOffsetAndLength(count);
		return View(start, length);
	}

	/// <summary>
	/// Returns a non-copying, read-only <see cref="RingBufferView{T}"/> over a slice of the current
	/// logical contents.
	/// </summary>
	/// <param name="start">
	/// Start of the slice.
	/// </param>
	/// <param name="length">
	/// Length of the slice.
	/// </param>
	public RingBufferView<T> View(int start, int length) {
		validateSlice(start, length);
		ulong capturedVersion = version;
		int physicalStart = physicalIndex(start);
		int firstLength = Math.Min(length, buf.Length - physicalStart);
		return new RingBufferView<T>(this, capturedVersion, buf.AsSpan(physicalStart, firstLength), buf.AsSpan(0, length - firstLength));
	}

	/// <summary>
	/// Returns an enumerator that iterates through the buffer from oldest to newest.
	/// </summary>
	public Enumerator GetEnumerator() => new(this);
	/// <inheritdoc cref="GetEnumerator()"/>
	IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
	/// <inheritdoc cref="GetEnumerator()"/>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	/// Returns an enumerable that enumerates the contents of the buffer from newest to oldest.
	/// </summary>
	public ReverseEnumerable EnumerateReverse() => new(this);

	// ==========================================================================
	// enumerators

	/// <summary>
	/// Enumerates a <see cref="RingBuffer{T}"/> from oldest to newest.
	/// </summary>
	/// <remarks>
	/// <para>
	/// No managed heap allocations are performed when this struct is not boxed to an interface.
	/// All individual operations are O(1), and complete enumeration is O(n).
	/// </para>
	/// <para>
	/// Any successful mutation of the underlying <see cref="RingBuffer{T}"/> causes invalidation.
	/// </para>
	/// <para>
	/// <see cref="Reset()"/> is supported and is also O(1).
	/// </para>
	/// </remarks>
	public struct Enumerator : IEnumerator<T> {
		private readonly RingBuffer<T> ringbuf;
		private readonly ulong version;
		private int idx;
		private int physicalIdx;

		internal Enumerator(RingBuffer<T> ringbuf) {
			this.ringbuf = ringbuf;
			version = ringbuf.version;
			idx = -1;
			physicalIdx = ringbuf.decr(ringbuf.head);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly void throwIfVerChanged() {
			if (version != ringbuf.version)
				throw new InvalidOperationException("collection was modified during enumeration");
		}

		public readonly T Current {
			get {
				throwIfVerChanged();
				return ringbuf.buf[physicalIdx];
			}
		}

		readonly object? IEnumerator.Current => Current;

		public bool MoveNext() {
			throwIfVerChanged();
			if (idx == ringbuf.count)
				return false;
			idx++;
			if (idx == ringbuf.count)
				return false;
			physicalIdx = ringbuf.incr(physicalIdx);
			return true;
		}

		public void Reset() {
			throwIfVerChanged();
			idx = -1;
			physicalIdx = ringbuf.decr(ringbuf.head);
		}

		public readonly void Dispose() {
		}
	}

	/// <summary>
	/// Enumerable for <see langword="foreach"/> enumeration of a <see cref="RingBuffer{T}"/>
	/// in reverse (newest to oldest) order.
	/// </summary>
	public readonly struct ReverseEnumerable : IEnumerable<T> {
		private readonly RingBuffer<T> ringbuf;

		internal ReverseEnumerable(RingBuffer<T> ringbuf) {
			this.ringbuf = ringbuf;
		}

		public ReverseEnumerator GetEnumerator() => new(ringbuf);
		IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	/// <summary>
	/// Enumerates a <see cref="RingBuffer{T}"/> from newest to oldest.
	/// </summary>
	/// <remarks>
	/// <para>
	/// No managed heap allocations are performed when this struct is not boxed to an interface.
	/// All individual operations are O(1), and complete enumeration is O(n).
	/// </para>
	/// <para>
	/// Any successful mutation of the underlying <see cref="RingBuffer{T}"/> causes invalidation.
	/// </para>
	/// <para>
	/// <see cref="Reset()"/> is supported and is also O(1).
	/// </para>
	/// </remarks>
	public struct ReverseEnumerator : IEnumerator<T> {
		private readonly RingBuffer<T> ringbuf;
		private readonly ulong version;
		private int idx;
		private int physicalIdx;

		internal ReverseEnumerator(RingBuffer<T> ringbuf) {
			this.ringbuf = ringbuf;
			version = ringbuf.version;
			idx = ringbuf.count;
			physicalIdx = ringbuf.physicalIndex(ringbuf.count);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly void throwIfVerChanged() {
			if (version != ringbuf.version)
				throw new InvalidOperationException("collection was modified during enumeration");
		}

		public readonly T Current {
			get {
				throwIfVerChanged();
				return ringbuf.buf[physicalIdx];
			}
		}

		readonly object? IEnumerator.Current => Current;

		public bool MoveNext() {
			throwIfVerChanged();
			if (idx < 0)
				return false;
			idx--;
			if (idx < 0)
				return false;
			physicalIdx = ringbuf.decr(physicalIdx);
			return true;
		}

		public void Reset() {
			throwIfVerChanged();
			idx = ringbuf.count;
			physicalIdx = ringbuf.physicalIndex(ringbuf.count);
		}

		public readonly void Dispose() {
		}
	}

	// ==========================================================================
	// debug view
	private sealed class RingBufferDebuggerTypeProxy<TItem>(RingBuffer<TItem> ringbuf) {
		[DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
		public TItem[] Items => ringbuf.ToArray();

		public int Count => ringbuf.Count;
		public int Capacity => ringbuf.Capacity;
	}
}

/// <summary>
/// A read-only, non-owning view of a captured logical range of a <see cref="RingBuffer{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// This type does not copy elements. It captures the logical ordering and range present
/// when it is created.
/// </para>
/// <para>
/// Any subsequent successful mutation of the originating buffer invalidates the view. Accessing
/// an invalidated view throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// The view is shallowly read-only; reference types may still be mutated independently, etc.
/// </para>
/// </remarks>
public readonly ref struct RingBufferView<T> {
	private readonly RingBuffer<T> owner;
	private readonly ulong version;
	private readonly ReadOnlySpan<T> first;
	private readonly ReadOnlySpan<T> second;
	private readonly int count;

	internal RingBufferView(RingBuffer<T> owner, ulong version, ReadOnlySpan<T> first, ReadOnlySpan<T> second) {
		this.owner = owner;
		this.version = version;
		this.first = first;
		this.second = second;
		count = first.Length + second.Length;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void throwIfInvalid() {
		if (owner is not null && owner.Version != version)
			throw new InvalidOperationException("originating ring buffer was modified");
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private T itemUnchecked(int index) => index < first.Length ? first[index] : second[index - first.Length];

	private RingBufferView<T> sliceUnchecked(int start, int length) {
		if (start < first.Length) {
			int firstLength = Math.Min(length, first.Length - start);
			return new RingBufferView<T>(owner, version, first.Slice(start, firstLength), second.Slice(0, length - firstLength));
		}
		return new RingBufferView<T>(owner, version, second.Slice(start - first.Length, length), ReadOnlySpan<T>.Empty);
	}

	/// <summary>
	/// The number of elements captured by the view.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the originating buffer has been modified since the view was created.
	/// </exception>
	public int Count {
		get {
			throwIfInvalid();
			return count;
		}
	}

	/// <summary>
	/// Whether the view exposes no elements.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the originating buffer has been modified since the view was created.
	/// </exception>
	public bool IsEmpty {
		get {
			throwIfInvalid();
			return count == 0;
		}
	}

	/// <summary>
	/// Gets the element at the specified logical index within the view.
	/// </summary>
	/// <param name="idx">
	/// The logical index, where <c>0</c> is the oldest element in the view.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="idx"/> is outside the bounds of the view.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the originating buffer has been modified since the view was created.
	/// </exception>
	public T this[int idx] {
		get {
			throwIfInvalid();
			if ((uint)idx >= (uint)count)
				throw new ArgumentOutOfRangeException(nameof(idx));
			return itemUnchecked(idx);
		}
	}

	/// <summary>
	/// Creates a view over a specified logical range within this view.
	/// </summary>
	/// <param name="range">
	/// The range to select, relative to this view.
	/// </param>
	/// <remarks>
	/// The returned view refers to the same originating buffer and is invalidated by the
	/// same mutations as this view.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="range"/> resolves outside the bounds of the view.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the originating buffer has been modified since the view was created.
	/// </exception>
	public RingBufferView<T> this[Range range] {
		get {
			throwIfInvalid();
			(int start, int length) = range.GetOffsetAndLength(count);
			return sliceUnchecked(start, length);
		}
	}

	/// <summary>
	/// Copies the contents of the view into the specified span.
	/// </summary>
	/// <param name="dst">The destination span.</param>
	/// <remarks>
	/// Elements are copied in the view's enumeration order (oldest to newest).
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="dst"/> is too small to be able to hold
	/// <see cref="Count"/> elements.
	/// </exception>
	public void CopyTo(Span<T> dst) {
		throwIfInvalid();
		if (dst.Length < count)
			throw new ArgumentException($"destination span is too small (length = {dst.Length}, expected at least {count})", nameof(dst));
		first.CopyTo(dst);
		second.CopyTo(dst[first.Length..]);
	}

	/// <summary>
	/// Returns a new array containing the elements in the view.
	/// </summary>
	/// <remarks>
	/// Elements are in the view's enumeration order (oldest to newest).
	/// </remarks>
	public T[] ToArray() {
		throwIfInvalid();
		if (count == 0)
			return Array.Empty<T>();
		var arr = new T[count];
		first.CopyTo(arr);
		second.CopyTo(arr.AsSpan(first.Length));
		return arr;
	}

	/// <summary>
	/// Returns an enumerator that iterates through the view from oldest to newest.
	/// </summary>
	public Enumerator GetEnumerator() => new(this);

	/// <summary>
	/// Returns an enumerable that enumerates the elements in the view from newest to oldest.
	/// </summary>
	public ReverseEnumerable EnumerateReverse() => new(this);

	/// <summary>
	/// Enumerates a <see cref="RingBufferView{T}"/> from oldest to newest.
	/// </summary>
	/// <remarks>
	/// <para>
	/// No managed heap allocations are performed. All individual operations are O(1),
	/// and complete enumeration is O(n).
	/// </para>
	/// <para>
	/// Any successful mutation of the originating <see cref="RingBuffer{T}"/> causes invalidation.
	/// </para>
	/// </remarks>
	public ref struct Enumerator {
		private readonly RingBufferView<T> view;
		private int index;

		internal Enumerator(RingBufferView<T> view) {
			this.view = view;
			index = -1;
		}

		public readonly T Current {
			get {
				view.throwIfInvalid();
				if ((uint)index >= (uint)view.count)
					throw new InvalidOperationException();
				return view.itemUnchecked(index);
			}
		}

		public bool MoveNext() {
			view.throwIfInvalid();
			if (index >= view.count)
				return false;
			index++;
			return index < view.count;
		}
	}

	/// <summary>
	/// Enumerable for <see langword="foreach"/> enumeration of a <see cref="RingBufferView{T}"/>
	/// in reverse (newest to oldest) order.
	/// </summary>
	public readonly ref struct ReverseEnumerable {
		private readonly RingBufferView<T> view;
		internal ReverseEnumerable(RingBufferView<T> view) {
			this.view = view;
		}

		public ReverseEnumerator GetEnumerator() => new(view);
	}

	/// <summary>
	/// Enumerates a <see cref="RingBufferView{T}"/> from newest to oldest.
	/// </summary>
	/// <remarks>
	/// <para>
	/// No managed heap allocations are performed. All individual operations are O(1),
	/// and complete enumeration is O(n).
	/// </para>
	/// <para>
	/// Any successful mutation of the originating <see cref="RingBuffer{T}"/> causes invalidation.
	/// </para>
	/// </remarks>
	public ref struct ReverseEnumerator {
		private readonly RingBufferView<T> view;
		private int index;

		internal ReverseEnumerator(RingBufferView<T> view) {
			this.view = view;
			index = view.count;
		}

		public readonly T Current {
			get {
				view.throwIfInvalid();
				if ((uint)index >= (uint)view.count)
					throw new InvalidOperationException();
				return view.itemUnchecked(index);
			}
		}

		public bool MoveNext() {
			view.throwIfInvalid();
			if (index < 0)
				return false;
			index--;
			return index >= 0;
		}
	}
}
