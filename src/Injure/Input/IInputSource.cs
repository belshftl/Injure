// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Input;

/// <summary>
/// Raw input state, plus a bounded history of raw input events that consumers read through cursors.
/// </summary>
/// <remarks>
/// <para>
/// Each consumer keeps its own <see cref="InputCursor"/> and reads the events added since its
/// cursor's position, so several consumers (e.g. layers) can each see every event once. The
/// history is bounded; a consumer that falls behind by more than the history's capacity gets a
/// view with <see cref="InputView.HistoryLost"/> set instead of the events it missed.
/// </para>
/// <para>
/// Unless an implementation documents otherwise, this interface is not thread-safe and is to be
/// used from the thread that feeds the source.
/// </para>
/// </remarks>
public interface IInputSource {
	/// <summary>
	/// A snapshot of the current device state.
	/// </summary>
	InputSnapshot CurrentState { get; }

	/// <summary>
	/// Creates a cursor positioned at the current end of the event history, i.e. one that sees
	/// only events added from now on.
	/// </summary>
	InputCursor CreateCursor();

	/// <summary>
	/// Creates a view of the events added since <paramref name="cursor"/>'s position and the current
	/// device state, without moving <paramref name="cursor"/>.
	/// </summary>
	/// <param name="cursor">Cursor to read from.</param>
	/// <param name="next">A cursor positioned at the current end of the event history.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="cursor"/> was not created by this source.
	/// </exception>
	InputView CreateView(InputCursor cursor, out InputCursor next);

	/// <summary>
	/// Creates a view of the events added since <paramref name="cursor"/>'s position and the current
	/// device state, then advances <paramref name="cursor"/> to the current end of the event history.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="cursor"/> was not created by this source.
	/// </exception>
	InputView CreateViewAndAdvance(ref InputCursor cursor);

	/// <summary>
	/// Advances <paramref name="cursor"/> to the current end of the event history, skipping the
	/// events in between.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="cursor"/> was not created by this source.
	/// </exception>
	void AdvanceToCurrent(ref InputCursor cursor);
}
