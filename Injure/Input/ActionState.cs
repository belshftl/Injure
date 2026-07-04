// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Numerics;

namespace Injure.Input;

public readonly record struct ButtonActionState(bool Down, bool PreviousDown) {
	public bool Pressed => Down && !PreviousDown;
	public bool Released => !Down && PreviousDown;
}

public readonly record struct StateAxisActionState(float Value, float PreviousValue) {
	public float StepDelta => Value - PreviousValue;
}

public readonly record struct StateAxis2DActionState(Vector2 Value, Vector2 PreviousValue) {
	public Vector2 StepDelta => Value - PreviousValue;
}

public readonly record struct ImpulseAxisActionState(float Amount);
public readonly record struct ButtonActionStateEntry(ActionId Action, ButtonActionState State);
public readonly record struct StateAxisActionStateEntry(ActionId Action, StateAxisActionState State);
public readonly record struct StateAxis2DActionStateEntry(ActionId Action, StateAxis2DActionState State);
public readonly record struct ImpulseAxisActionStateEntry(ActionId Action, ImpulseAxisActionState State);

public sealed class ActionStateSnapshot {
	public ImmutableDictionary<ActionId, ButtonActionState> Buttons { get; }
	public ImmutableDictionary<ActionId, StateAxisActionState> StateAxes { get; }
	public ImmutableDictionary<ActionId, StateAxis2DActionState> StateAxes2D { get; }
	public ImmutableDictionary<ActionId, ImpulseAxisActionState> ImpulseAxes { get; }

	public static readonly ActionStateSnapshot Empty = new(
		ImmutableDictionary<ActionId, ButtonActionState>.Empty,
		ImmutableDictionary<ActionId, StateAxisActionState>.Empty,
		ImmutableDictionary<ActionId, StateAxis2DActionState>.Empty,
		ImmutableDictionary<ActionId, ImpulseAxisActionState>.Empty
	);

	public ActionStateSnapshot(
		ReadOnlySpan<ButtonActionStateEntry> buttons,
		ReadOnlySpan<StateAxisActionStateEntry> stateAxes,
		ReadOnlySpan<StateAxis2DActionStateEntry> stateAxes2D,
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
		StateAxes2D = copy(stateAxes2D, static e => e.Action, static e => e.State, nameof(stateAxes2D));
		ImpulseAxes = copy(impulseAxes, static e => e.Action, static e => e.State, nameof(impulseAxes));
	}

	public ActionStateSnapshot(
		ImmutableDictionary<ActionId, ButtonActionState> buttons,
		ImmutableDictionary<ActionId, StateAxisActionState> stateAxes,
		ImmutableDictionary<ActionId, StateAxis2DActionState> stateAxes2D,
		ImmutableDictionary<ActionId, ImpulseAxisActionState> impulseAxes
	) {
		Buttons = buttons ?? throw new ArgumentNullException(nameof(buttons));
		StateAxes = stateAxes ?? throw new ArgumentNullException(nameof(stateAxes));
		StateAxes2D = stateAxes2D ?? throw new ArgumentNullException(nameof(stateAxes2D));
		ImpulseAxes = impulseAxes ?? throw new ArgumentNullException(nameof(impulseAxes));
	}

	public ButtonActionState GetButton(ActionId action) => Buttons.TryGetValue(action, out ButtonActionState state) ? state : default;
	public StateAxisActionState GetStateAxis(ActionId action) => StateAxes.TryGetValue(action, out StateAxisActionState state) ? state : default;
	public StateAxis2DActionState GetStateAxis2D(ActionId action) => StateAxes2D.TryGetValue(action, out StateAxis2DActionState state) ? state : default;
	public ImpulseAxisActionState GetImpulseAxis(ActionId action) => ImpulseAxes.TryGetValue(action, out ImpulseAxisActionState state) ? state : default;

	public ActionStateView AsView() => new(
		new ButtonActionStateView(Buttons),
		new StateAxisActionStateView(StateAxes),
		new StateAxis2DActionStateView(StateAxes2D),
		new ImpulseAxisActionStateView(ImpulseAxes)
	);
}

public readonly ref struct ButtonActionStateView {
	internal readonly IReadOnlyDictionary<ActionId, ButtonActionState> States;
	internal ButtonActionStateView(IReadOnlyDictionary<ActionId, ButtonActionState> states) {
		States = states;
	}
	public ButtonActionState this[ActionId action] => States.TryGetValue(action, out ButtonActionState state) ? state : default;
}

public readonly ref struct StateAxisActionStateView {
	internal readonly IReadOnlyDictionary<ActionId, StateAxisActionState> States;
	internal StateAxisActionStateView(IReadOnlyDictionary<ActionId, StateAxisActionState> states) {
		States = states;
	}
	public StateAxisActionState this[ActionId action] => States.TryGetValue(action, out StateAxisActionState state) ? state : default;
}

public readonly ref struct StateAxis2DActionStateView {
	internal readonly IReadOnlyDictionary<ActionId, StateAxis2DActionState> States;
	internal StateAxis2DActionStateView(IReadOnlyDictionary<ActionId, StateAxis2DActionState> states) {
		States = states;
	}
	public StateAxis2DActionState this[ActionId action] => States.TryGetValue(action, out StateAxis2DActionState state) ? state : default;
}

public readonly ref struct ImpulseAxisActionStateView {
	internal readonly IReadOnlyDictionary<ActionId, ImpulseAxisActionState> States;
	internal ImpulseAxisActionStateView(IReadOnlyDictionary<ActionId, ImpulseAxisActionState> states) {
		States = states;
	}
	public ImpulseAxisActionState this[ActionId action] => States.TryGetValue(action, out ImpulseAxisActionState state) ? state : default;
}

public readonly ref struct ActionStateView {
	public ButtonActionStateView Buttons { get; }
	public StateAxisActionStateView StateAxes { get; }
	public StateAxis2DActionStateView StateAxes2D { get; }
	public ImpulseAxisActionStateView ImpulseAxes { get; }

	public static ActionStateView Empty => ActionStateSnapshot.Empty.AsView();

	internal ActionStateView(ButtonActionStateView buttons, StateAxisActionStateView stateAxes, StateAxis2DActionStateView stateAxes2D, ImpulseAxisActionStateView impulseAxes) {
		Buttons = buttons;
		StateAxes = stateAxes;
		StateAxes2D = stateAxes2D;
		ImpulseAxes = impulseAxes;
	}

	public ActionStateSnapshot ToSnapshot() => new(
		Buttons.States.ToImmutableDictionary(),
		StateAxes.States.ToImmutableDictionary(),
		StateAxes2D.States.ToImmutableDictionary(),
		ImpulseAxes.States.ToImmutableDictionary()
	);
}
