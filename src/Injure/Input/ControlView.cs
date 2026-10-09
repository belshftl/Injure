// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;

namespace Injure.Input;

/// <summary>
/// The result of one <see cref="ActionTracker.Update(HostTick, in InputView)"/>: action states, the
/// control events of the step, and the raw device state.
/// </summary>
/// <remarks>
/// <para>
/// Prefer actions (<see cref="Actions"/>, <see cref="Events"/>) over the raw device state, since
/// they respect the player's bindings; the raw state is meant for things bindings can't express,
/// such as showing which physical key is held.
/// </para>
/// <para>
/// Only valid until the next <see cref="ActionTracker.Update(HostTick, in InputView)"/> on the same
/// tracker.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is an empty view: no action states, no events,
/// and the resting device state.
/// </para>
/// </remarks>
public readonly ref struct ControlView {
	/// <summary>
	/// States of all actions bound in the tracker's current map.
	/// </summary>
	public ActionStateView Actions { get; }

	/// <summary>
	/// Control events of this step, in order.
	/// </summary>
	public ReadOnlySpan<ControlEvent> Events { get; }

	/// <summary>
	/// Raw keyboard state at the end of this step.
	/// </summary>
	public KeyboardState RawKeyboard { get; }

	/// <summary>
	/// Raw pointer state at the end of this step.
	/// </summary>
	public PointerState RawPointer { get; }

	/// <summary>
	/// Raw states of all connected gamepads at the end of this step.
	/// </summary>
	public GamepadStateSet RawGamepads { get; }

	internal ControlView(ActionStateView actions, ReadOnlySpan<ControlEvent> events, InputSnapshot raw) {
		Actions = actions;
		Events = events;
		RawKeyboard = raw.Keyboard;
		RawPointer = raw.Pointer;
		RawGamepads = raw.Gamepads;
	}
}
