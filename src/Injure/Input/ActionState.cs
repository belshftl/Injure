// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Numerics;

namespace Injure.Input;

/// <summary>
/// A button action's state at the end of a step, plus at the end of the previous step.
/// </summary>
/// <remarks>
/// <para>
/// A press + release within the same step leaves both <see cref="Down"/> and
/// <see cref="PreviousDown"/> unchanged; use the step's control events to catch those.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is a button that is and was up.
/// </para>
/// </remarks>
public readonly record struct ButtonActionState(bool Down, bool PreviousDown) {
	/// <summary>
	/// Whether the button is down but was up last step, i.e. went down this step.
	/// </summary>
	public bool Pressed => Down && !PreviousDown;

	/// <summary>
	/// Whether the button is up but was down last step, i.e. went up this step.
	/// </summary>
	public bool Released => !Down && PreviousDown;
}

/// <summary>
/// A 1D state axis action's value at the end of a step, plus at the end of the previous step.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is an axis that is and was at 0.
/// </remarks>
public readonly record struct StateAxisActionState(float Value, float PreviousValue) {
	/// <summary>
	/// How much the value changed during the step.
	/// </summary>
	public float StepDelta => Value - PreviousValue;
}

/// <summary>
/// A 2D state axis action's value at the end of a step, plus at the end of the previous step.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is an axis that is and was at (0, 0).
/// </remarks>
public readonly record struct StateAxis2dActionState(Vector2 Value, Vector2 PreviousValue) {
	/// <summary>
	/// How much the value changed during the step.
	/// </summary>
	public Vector2 StepDelta => Value - PreviousValue;
}

/// <summary>
/// An impulse axis action's total amount during a step.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is no impulse.
/// </remarks>
public readonly record struct ImpulseAxisActionState(float Amount);

/// <summary>An action and its <see cref="ButtonActionState"/>.</summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Action"/> is.
/// </remarks>
public readonly record struct ButtonActionStateEntry(ActionId Action, ButtonActionState State);

/// <summary>An action and its <see cref="StateAxisActionState"/>.</summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Action"/> is.
/// </remarks>
public readonly record struct StateAxisActionStateEntry(ActionId Action, StateAxisActionState State);

/// <summary>An action and its <see cref="StateAxis2dActionState"/>.</summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Action"/> is.
/// </remarks>
public readonly record struct StateAxis2dActionStateEntry(ActionId Action, StateAxis2dActionState State);

/// <summary>An action and its <see cref="ImpulseAxisActionState"/>.</summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Action"/> is.
/// </remarks>
public readonly record struct ImpulseAxisActionStateEntry(ActionId Action, ImpulseAxisActionState State);

/// <summary>
/// An immutable copy of action states, e.g. from <see cref="ActionStateView.ToSnapshot()"/>, for
/// keeping beyond the step.
/// </summary>
public sealed class ActionStateSnapshot {
	/// <summary>Button action states.</summary>
	public ImmutableDictionary<ActionId, ButtonActionState> Buttons { get; }

	/// <summary>1D state axis action states.</summary>
	public ImmutableDictionary<ActionId, StateAxisActionState> StateAxes { get; }

	/// <summary>2D state axis action states.</summary>
	public ImmutableDictionary<ActionId, StateAxis2dActionState> StateAxes2d { get; }

	/// <summary>Impulse axis action states; only actions with a nonzero amount are present.</summary>
	public ImmutableDictionary<ActionId, ImpulseAxisActionState> ImpulseAxes { get; }

	/// <summary>
	/// A snapshot with no action states.
	/// </summary>
	public static readonly ActionStateSnapshot Empty = new(
		ImmutableDictionary<ActionId, ButtonActionState>.Empty,
		ImmutableDictionary<ActionId, StateAxisActionState>.Empty,
		ImmutableDictionary<ActionId, StateAxis2dActionState>.Empty,
		ImmutableDictionary<ActionId, ImpulseAxisActionState>.Empty
	);

	/// <summary>
	/// Creates a snapshot from copies of the given entries.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if an entry list contains an action more than once.
	/// </exception>
	public ActionStateSnapshot(
		ReadOnlySpan<ButtonActionStateEntry> buttons,
		ReadOnlySpan<StateAxisActionStateEntry> stateAxes,
		ReadOnlySpan<StateAxis2dActionStateEntry> stateAxes2d,
		ReadOnlySpan<ImpulseAxisActionStateEntry> impulseAxes
	) {
		static ImmutableDictionary<ActionId, TValue> copy<TEntry, TValue>(
			ReadOnlySpan<TEntry> entries,
			Func<TEntry, ActionId> getAction,
			Func<TEntry, TValue> getValue,
			string paramName
		) {
			Dictionary<ActionId, TValue> ret = new(entries.Length);
			foreach (TEntry entry in entries) {
				ActionId action = getAction(entry);
				if (!ret.TryAdd(action, getValue(entry)))
					throw new ArgumentException("action state entries must not contain duplicate actions", paramName);
			}
			return ret.ToImmutableDictionary();
		}

		Buttons = copy(buttons, static e => e.Action, static e => e.State, nameof(buttons));
		StateAxes = copy(stateAxes, static e => e.Action, static e => e.State, nameof(stateAxes));
		StateAxes2d = copy(stateAxes2d, static e => e.Action, static e => e.State, nameof(stateAxes2d));
		ImpulseAxes = copy(impulseAxes, static e => e.Action, static e => e.State, nameof(impulseAxes));
	}

	/// <summary>
	/// Creates a snapshot from the given dictionaries.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if any dictionary is <see langword="null"/>.
	/// </exception>
	public ActionStateSnapshot(
		ImmutableDictionary<ActionId, ButtonActionState> buttons,
		ImmutableDictionary<ActionId, StateAxisActionState> stateAxes,
		ImmutableDictionary<ActionId, StateAxis2dActionState> stateAxes2d,
		ImmutableDictionary<ActionId, ImpulseAxisActionState> impulseAxes
	) {
		Buttons = buttons ?? throw new ArgumentNullException(nameof(buttons));
		StateAxes = stateAxes ?? throw new ArgumentNullException(nameof(stateAxes));
		StateAxes2d = stateAxes2d ?? throw new ArgumentNullException(nameof(stateAxes2d));
		ImpulseAxes = impulseAxes ?? throw new ArgumentNullException(nameof(impulseAxes));
	}

	/// <summary>
	/// Gets a button action's state, or <see langword="default"/> if not present.
	/// </summary>
	public ButtonActionState GetButton(ActionId action) =>
		Buttons.TryGetValue(action, out ButtonActionState state) ? state : default;

	/// <summary>
	/// Gets a 1D state axis action's state, or <see langword="default"/> if not present.
	/// </summary>
	public StateAxisActionState GetStateAxis(ActionId action) =>
		StateAxes.TryGetValue(action, out StateAxisActionState state) ? state : default;

	/// <summary>
	/// Gets a 2D state axis action's state, or <see langword="default"/> if not present.
	/// </summary>
	public StateAxis2dActionState GetStateAxis2d(ActionId action) =>
		StateAxes2d.TryGetValue(action, out StateAxis2dActionState state) ? state : default;

	/// <summary>
	/// Gets an impulse axis action's state, or <see langword="default"/> if not present.
	/// </summary>
	public ImpulseAxisActionState GetImpulseAxis(ActionId action) =>
		ImpulseAxes.TryGetValue(action, out ImpulseAxisActionState state) ? state : default;

	/// <summary>
	/// Returns a view over this snapshot.
	/// </summary>
	public ActionStateView AsView() => new(
		new ButtonActionStateView(Buttons),
		new StateAxisActionStateView(StateAxes),
		new StateAxis2dActionStateView(StateAxes2d),
		new ImpulseAxisActionStateView(ImpulseAxes)
	);
}

/// <summary>
/// Read-only access to button action states.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and contains no states.
/// </remarks>
public readonly ref struct ButtonActionStateView {
	/// <remarks>
	/// <see langword="null"/> only for the <see langword="default"/> value, which behaves as empty.
	/// </remarks>
	internal readonly IReadOnlyDictionary<ActionId, ButtonActionState>? States;
	internal ButtonActionStateView(IReadOnlyDictionary<ActionId, ButtonActionState> states) {
		States = states;
	}

	/// <summary>
	/// Gets an action's state, or <see langword="default"/> if not present.
	/// </summary>
	public ButtonActionState this[ActionId action] =>
		States is not null && States.TryGetValue(action, out ButtonActionState state) ? state : default;

	internal ImmutableDictionary<ActionId, ButtonActionState> ToImmutable() =>
		States?.ToImmutableDictionary() ?? ImmutableDictionary<ActionId, ButtonActionState>.Empty;
}

/// <summary>
/// Read-only access to 1D state axis action states.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and contains no states.
/// </remarks>
public readonly ref struct StateAxisActionStateView {
	/// <remarks>
	/// <see langword="null"/> only for the <see langword="default"/> value, which behaves as empty.
	/// </remarks>
	internal readonly IReadOnlyDictionary<ActionId, StateAxisActionState>? States;
	internal StateAxisActionStateView(IReadOnlyDictionary<ActionId, StateAxisActionState> states) {
		States = states;
	}

	/// <summary>
	/// Gets an action's state, or <see langword="default"/> if not present.
	/// </summary>
	public StateAxisActionState this[ActionId action] =>
		States is not null && States.TryGetValue(action, out StateAxisActionState state) ? state : default;

	internal ImmutableDictionary<ActionId, StateAxisActionState> ToImmutable() =>
		States?.ToImmutableDictionary() ?? ImmutableDictionary<ActionId, StateAxisActionState>.Empty;
}

/// <summary>
/// Read-only access to 2D state axis action states.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and contains no states.
/// </remarks>
public readonly ref struct StateAxis2dActionStateView {
	/// <remarks>
	/// <see langword="null"/> only for the <see langword="default"/> value, which behaves as empty.
	/// </remarks>
	internal readonly IReadOnlyDictionary<ActionId, StateAxis2dActionState>? States;
	internal StateAxis2dActionStateView(IReadOnlyDictionary<ActionId, StateAxis2dActionState> states) {
		States = states;
	}

	/// <summary>
	/// Gets an action's state, or <see langword="default"/> if not present.
	/// </summary>
	public StateAxis2dActionState this[ActionId action] =>
		States is not null && States.TryGetValue(action, out StateAxis2dActionState state) ? state : default;

	internal ImmutableDictionary<ActionId, StateAxis2dActionState> ToImmutable() =>
		States?.ToImmutableDictionary() ?? ImmutableDictionary<ActionId, StateAxis2dActionState>.Empty;
}

/// <summary>
/// Read-only access to impulse axis action states.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and contains no states.
/// </remarks>
public readonly ref struct ImpulseAxisActionStateView {
	/// <remarks>
	/// <see langword="null"/> only for the <see langword="default"/> value, which behaves as empty.
	/// </remarks>
	internal readonly IReadOnlyDictionary<ActionId, ImpulseAxisActionState>? States;
	internal ImpulseAxisActionStateView(IReadOnlyDictionary<ActionId, ImpulseAxisActionState> states) {
		States = states;
	}

	/// <summary>
	/// Gets an action's state, or <see langword="default"/> if not present.
	/// </summary>
	public ImpulseAxisActionState this[ActionId action] => States is not null && States.TryGetValue(action, out ImpulseAxisActionState state) ? state : default;

	internal ImmutableDictionary<ActionId, ImpulseAxisActionState> ToImmutable() =>
		States?.ToImmutableDictionary() ?? ImmutableDictionary<ActionId, ImpulseAxisActionState>.Empty;
}

/// <summary>
/// Read-only access to the states of all actions.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and contains no states, same as
/// <see cref="Empty"/>.
/// </remarks>
public readonly ref struct ActionStateView {
	/// <summary>Button action states.</summary>
	public ButtonActionStateView Buttons { get; }

	/// <summary>1D state axis action states.</summary>
	public StateAxisActionStateView StateAxes { get; }

	/// <summary>2D state axis action states.</summary>
	public StateAxis2dActionStateView StateAxes2d { get; }

	/// <summary>Impulse axis action states.</summary>
	public ImpulseAxisActionStateView ImpulseAxes { get; }

	/// <summary>A view with no states.</summary>
	public static ActionStateView Empty => default;

	internal ActionStateView(ButtonActionStateView buttons, StateAxisActionStateView stateAxes, StateAxis2dActionStateView stateAxes2d, ImpulseAxisActionStateView impulseAxes) {
		Buttons = buttons;
		StateAxes = stateAxes;
		StateAxes2d = stateAxes2d;
		ImpulseAxes = impulseAxes;
	}

	/// <summary>
	/// Copies the states into an <see cref="ActionStateSnapshot"/>.
	/// </summary>
	public ActionStateSnapshot ToSnapshot() => new(
		Buttons.ToImmutable(),
		StateAxes.ToImmutable(),
		StateAxes2d.ToImmutable(),
		ImpulseAxes.ToImmutable()
	);
}
