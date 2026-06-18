// SPDX-License-Identifier: MIT

namespace Injure.Input;

/// <summary>
/// Provides cursor-based access to raw input events and device state.
/// </summary>
/// <remarks>
/// Unless an implementation documents otherwise, this interface is not thread-safe and
/// should be accessed from the host/input thread.
/// </remarks>
public interface IInputSource {
	/// <summary>
	/// Gets a snapshot of the current input-device state.
	/// </summary>
	InputSnapshot CurrentState { get; }

	/// <summary>
	/// Creates a cursor positioned at the current end of the event history.
	/// </summary>
	InputCursor CreateCursor();

	/// <summary>
	/// Creates a view containing input accumulated since the cursor's previous position,
	/// then advances the cursor to the current end of the event history.
	/// </summary>
	InputView CreateViewSince(ref InputCursor cursor);

	/// <summary>
	/// Advances a cursor to the current end of the event history without returning events.
	/// </summary>
	void AdvanceToCurrent(ref InputCursor cursor);
}
