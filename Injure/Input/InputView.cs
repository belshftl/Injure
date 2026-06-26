// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

using Injure.Collections;

namespace Injure.Input;

public readonly struct InputSnapshot(KeyboardState keyboard, PointerState pointer, GamepadStateSet gamepads) {
	public static readonly InputSnapshot Rest = default;

	public KeyboardState Keyboard { get; } = keyboard;
	public PointerState Pointer { get; } = pointer;
	public GamepadStateSet Gamepads { get; } = gamepads;
}

/// <summary>
/// A read-only sequence of input events.
/// </summary>
/// <remarks>
/// A <see langword="default"/> value represents an empty sequence.
/// Non-empty instances may alias mutable input-system storage and must not be retained
/// beyond the current input-processing phase.
/// </remarks>
public readonly ref struct InputEventView {
	private readonly RingBufferView<InputEvent> ringView;
	private readonly bool hasRingView;

	internal InputEventView(RingBufferView<InputEvent> ringView) {
		this.ringView = ringView;
		hasRingView = true;
	}

	/// <summary>
	/// An empty event view.
	/// </summary>
	public static InputEventView Empty => default;

	/// <summary>
	/// The number of events in the view.
	/// </summary>
	public int Count => hasRingView ? ringView.Count : 0;

	/// <summary>
	/// Whether the view contains no events.
	/// </summary>
	public bool IsEmpty => Count == 0;

	/// <summary>
	/// Gets an event by its oldest-to-newest index.
	/// </summary>
	public InputEvent this[int idx] {
		get {
			if (!hasRingView)
				throw new ArgumentOutOfRangeException(nameof(idx));
			return ringView[idx];
		}
	}

	/// <summary>
	/// Returns an enumerator over the events from oldest to newest.
	/// </summary>
	public Enumerator GetEnumerator() => new(this);

	public ref struct Enumerator {
		private readonly InputEventView view;
		private int idx;

		internal Enumerator(InputEventView view) {
			this.view = view;
			idx = -1;
		}

		public readonly InputEvent Current => view[idx];

		public bool MoveNext() {
			if (idx == view.Count)
				return false;
			idx++;
			return idx != view.Count;
		}
	}
}

/// <summary>
/// A borrowed view of raw input events and current device state.
/// </summary>
/// <remarks>
/// <para>
/// The event view aliases the input source's bounded event storage. Any subsequent mutation
/// of that storage invalidates this view.
/// </para>
/// <para>
/// The view must be consumed synchronously and not stored beyond some input-processing phase.
/// </para>
/// </remarks>
public readonly ref struct InputView {
	/// <summary>
	/// An empty input view.
	/// </summary>
	public static InputView Empty => new(InputEventView.Empty, InputSnapshot.Rest, false, 0);

	/// <summary>
	/// Creates an input view with no captured events and the given device state.
	/// </summary>
	public static InputView EmptyWith(InputSnapshot state) => new(InputEventView.Empty, state, false, 0);

	/// <summary>
	/// Raw input events captured by this view.
	/// </summary>
	public InputEventView Events { get; }

	/// <summary>
	/// Device state at the time this view was created.
	/// </summary>
	public InputSnapshot State { get; }

	/// <summary>
	/// Whether one or more events requested by the cursor had already been overwritten.
	/// </summary>
	public bool HistoryLost { get; }

	/// <summary>
	/// The number of overwritten events that could not be returned.
	/// </summary>
	public ulong LostEventCount { get; }

	internal InputView(InputEventView events, InputSnapshot state, bool historyLost, ulong lostEventCount) {
		Events = events;
		State = state;
		HistoryLost = historyLost;
		LostEventCount = lostEventCount;
	}
}
