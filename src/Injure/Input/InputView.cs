// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Collections;

namespace Injure.Input;

/// <summary>
/// The state of all input devices at one point in time.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is the resting state of every device: no keys
/// or buttons held, no gamepads connected. Same as <see cref="Rest"/>.
/// </remarks>
public readonly struct InputSnapshot(KeyboardState keyboard, PointerState pointer, GamepadStateSet gamepads) {
	/// <summary>
	/// The resting state of every device.
	/// </summary>
	public static readonly InputSnapshot Rest = default;

	/// <summary>Keyboard state.</summary>
	public KeyboardState Keyboard { get; } = keyboard;

	/// <summary>Pointer state.</summary>
	public PointerState Pointer { get; } = pointer;

	/// <summary>States of all connected gamepads.</summary>
	public GamepadStateSet Gamepads { get; } = gamepads;
}

/// <summary>
/// A read-only sequence of raw input events, oldest first.
/// </summary>
/// <remarks>
/// <para>
/// Aliases the input source's event history, so it is only valid until the source records its next
/// event; reading it after that gives unspecified results.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is the empty sequence, same as
/// <see cref="Empty"/>.
/// </para>
/// </remarks>
public readonly ref struct InputEventView {
	private readonly RingView<InputEvent> ringView;
	private readonly bool hasRingView;

	internal InputEventView(RingView<InputEvent> ringView) {
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
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="idx"/> is out of range.
	/// </exception>
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

	/// <summary>
	/// Enumerator over an <see cref="InputEventView"/>.
	/// </summary>
	/// <remarks>
	/// The <see langword="default"/> value is valid and enumerates nothing.
	/// </remarks>
	public ref struct Enumerator {
		private readonly InputEventView view;
		private int idx;

		internal Enumerator(InputEventView view) {
			this.view = view;
			idx = -1;
		}

		/// <summary>The current event.</summary>
		public readonly InputEvent Current => view[idx];

		/// <summary>Advances to the next event.</summary>
		public bool MoveNext() {
			if (idx == view.Count)
				return false;
			idx++;
			return idx != view.Count;
		}
	}
}

/// <summary>
/// The raw input events a cursor hasn't seen yet, plus the device state at the time the view was
/// created.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Events"/> aliases the input source's event history and is only valid until the source
/// records its next event; see <see cref="InputEventView"/>.
/// </para>
/// <para>
/// If the cursor fell too far behind, <see cref="HistoryLost"/> is set and <see cref="Events"/> is
/// empty; the consumer is expected to resync from <see cref="State"/> instead.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is an empty view of the resting state, same as
/// <see cref="Empty"/>.
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
	/// Whether events the cursor hadn't seen yet were already gone from the history (overwritten,
	/// or discarded with <see cref="InputSystem.DiscardHistory()"/>). If set, <see cref="Events"/> is
	/// empty.
	/// </summary>
	public bool HistoryLost { get; }

	/// <summary>
	/// The number of events that were gone from the history; 0 unless <see cref="HistoryLost"/>.
	/// </summary>
	public ulong LostEventCount { get; }

	internal InputView(InputEventView events, InputSnapshot state, bool historyLost, ulong lostEventCount) {
		Events = events;
		State = state;
		HistoryLost = historyLost;
		LostEventCount = lostEventCount;
	}
}
