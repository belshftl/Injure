// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Injure.Host;

namespace Injure.Input;

/// <summary>
/// Evaluates actions for one consumer (e.g. a layer), turning raw input from an
/// <see cref="IInputSource"/> into action states and control events according to an
/// <see cref="ActionProfile"/>'s current map.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Update"/> once per step with the input the consumer hasn't seen yet. Button
/// actions are edge-driven from raw events; state axes are evaluated from the device state at the
/// end of the step; impulse axes accumulate within the step.
/// </para>
/// <para>
/// If the profile's map changes or the input view reports lost history, this context resyncs from
/// the current device state: every button action that was held gets a release, then every button
/// action whose bound inputs are currently held gets a press, then every state axis that was active
/// or is bound emits exactly one event with its current value (0 for now-unbound axes).
/// </para>
/// <para>
/// Gamepad bindings currently match any connected gamepad: a gamepad button action is held while
/// any gamepad holds the button, and a gamepad axis or stick reads the most deflected gamepad. This
/// is a known limitation; per-gamepad bindings will be added before the first stable release.
/// </para>
/// <para>
/// Not thread-safe.
/// </para>
/// </remarks>
public sealed class ActionCtx(ActionProfile profile) {
	private sealed class Lookup {
		public readonly Dictionary<InputButtonSource, List<ActionId>> ButtonActionsBySource = new();
		public readonly Dictionary<InputImpulseAxisSource, List<ImpulseAxisBinding>> ImpulseAxesBySource = new();
		public readonly HashSet<InputButtonSource> TrackedButtonSources = new();
		public readonly HashSet<ActionId> ButtonActions = new();
		public readonly HashSet<ActionId> StateAxisActions = new();
		public readonly HashSet<ActionId> StateAxis2dActions = new();
		public readonly HashSet<ActionId> ImpulseAxisActions = new();
	}

	private readonly ActionProfile profile = profile ?? throw new ArgumentNullException(nameof(profile));

	private ActionMapSnapshot map = profile.Current;
	private ulong mapVersion = 0;
	private Lookup lookup = new();

	private ulong nextPressStamp = 1;

	/// <summary>
	/// Which gamepads hold each gamepad button. A gamepad button source counts as down while its set is
	/// nonempty.
	/// </summary>
	private readonly Dictionary<GamepadButton, HashSet<GamepadId>> gamepadButtonHolders = new();
	private readonly Dictionary<InputButtonSource, bool> buttonSourceDown = new();
	private readonly Dictionary<InputButtonSource, ulong> buttonSourcePressedAt = new();
	private readonly Dictionary<ActionId, int> buttonHeldCounts = new();
	private readonly Dictionary<ActionId, bool> previousButtonDown = new();

	private readonly Dictionary<ActionId, float> stateAxisValues = new();
	private readonly Dictionary<ActionId, Vector2> stateAxis2dValues = new();

	private readonly Dictionary<ActionId, ButtonActionState> buttonStates = new();
	private readonly Dictionary<ActionId, StateAxisActionState> stateAxisStates = new();
	private readonly Dictionary<ActionId, StateAxis2dActionState> stateAxis2dStates = new();
	private readonly Dictionary<ActionId, ImpulseAxisActionState> impulseAxisStates = new();

	private readonly Dictionary<ActionId, float> stepImpulseAmounts = new();

	private readonly List<ControlEvent> events = new();

	private bool forceEmitStateAxes;
	private bool forceEmitStateAxes2d;

	/// <summary>
	/// Advances by one step, consuming <paramref name="input"/>.
	/// </summary>
	/// <param name="tick">Timestamp for events this step generates itself (resync).</param>
	/// <param name="input">The raw input since the previous step.</param>
	/// <returns>
	/// The action states and control events of this step; only valid until the next call.
	/// </returns>
	public ControlView Update(HostTick tick, in InputView input) {
		events.Clear();
		buttonStates.Clear();
		stateAxisStates.Clear();
		stateAxis2dStates.Clear();
		impulseAxisStates.Clear();
		stepImpulseAmounts.Clear();

		ulong version = profile.Version;
		bool mapChanged = version != mapVersion;
		if (mapChanged || input.HistoryLost)
			resync(tick, input.State, mapChanged ? version : null);

		foreach (InputEvent ev in input.Events)
			processInputEvent(ev);

		evaluateButtonsFinal();
		evaluateStateAxesFinal(tick, input.State);
		evaluateStateAxes2dFinal(tick, input.State);
		evaluateImpulseAxesFinal();

		forceEmitStateAxes = false;
		forceEmitStateAxes2d = false;

		ActionStateView actions = new(
			new ButtonActionStateView(buttonStates),
			new StateAxisActionStateView(stateAxisStates),
			new StateAxis2dActionStateView(stateAxis2dStates),
			new ImpulseAxisActionStateView(impulseAxisStates)
		);
		return new ControlView(actions, CollectionsMarshal.AsSpan(events), input.State);
	}

	/// <summary>
	/// Releases every held button action, optionally switches to the profile's current map, then
	/// re-derives what's held from the device state and forces every state axis to emit its value.
	/// </summary>
	private void resync(HostTick tick, InputSnapshot raw, ulong? newMapVersion) {
		synthesizeButtonReleases(tick);

		if (newMapVersion is ulong version) {
			map = profile.Current;
			mapVersion = version;
			lookup = makeLookup(map);
		}

		buttonSourceDown.Clear();
		buttonSourcePressedAt.Clear();
		buttonHeldCounts.Clear();
		gamepadButtonHolders.Clear();
		foreach (GamepadStateEntry ent in raw.Gamepads)
			foreach (GamepadButton button in GamepadButton.Enum.Values)
				if (ent.State.IsDown(button))
					getHolders(button).Add(ent.Id);

		baselineButtonsFromCurrentState(tick, raw);

		forceEmitStateAxes = true;
		forceEmitStateAxes2d = true;
	}

	/// <summary>
	/// Emits a release for every currently-held button action.
	/// </summary>
	/// <remarks>
	/// State axes need no equivalent, since a resync forces every axis that was active or is bound to
	/// emit its value once during evaluation, or 0 for now-unbound axes.
	/// </remarks>
	private void synthesizeButtonReleases(HostTick tick) {
		foreach ((ActionId action, bool down) in previousButtonDown)
			if (down)
				events.Add(new ButtonActionEvent(tick, action, EdgeType.Release));
	}

	private void baselineButtonsFromCurrentState(HostTick tick, InputSnapshot raw) {
		foreach (InputButtonSource source in lookup.TrackedButtonSources) {
			if (!isButtonSourceDown(raw, source))
				continue;

			buttonSourceDown[source] = true;
			buttonSourcePressedAt[source] = nextPressStamp++;

			if (!lookup.ButtonActionsBySource.TryGetValue(source, out List<ActionId>? actions))
				continue;

			foreach (ActionId action in actions) {
				int held = buttonHeldCounts.TryGetValue(action, out int h) ? h : 0;
				buttonHeldCounts[action] = held + 1;
				events.Add(new ButtonActionEvent(tick, action, EdgeType.Press));
			}
		}
	}

	private static Lookup makeLookup(ActionMapSnapshot map) {
		Lookup ret = new();

		foreach (ButtonBinding b in map.ButtonBindings) {
			if (!ret.ButtonActionsBySource.TryGetValue(b.Source, out List<ActionId>? list)) {
				list = new List<ActionId>();
				ret.ButtonActionsBySource.Add(b.Source, list);
			}
			list.Add(b.Action);
			ret.TrackedButtonSources.Add(b.Source);
			ret.ButtonActions.Add(b.Action);
		}

		foreach (StateAxisBinding b in map.StateAxisBindings) {
			ret.StateAxisActions.Add(b.Action);
			addTrackedSources(ret, b.Source);
		}

		foreach (StateAxis2dBinding b in map.StateAxis2dBindings) {
			ret.StateAxis2dActions.Add(b.Action);
			addTrackedSources(ret, b.Source);
		}

		foreach (ImpulseAxisBinding b in map.ImpulseAxisBindings) {
			if (!ret.ImpulseAxesBySource.TryGetValue(b.Source, out List<ImpulseAxisBinding>? list)) {
				list = new List<ImpulseAxisBinding>();
				ret.ImpulseAxesBySource.Add(b.Source, list);
			}
			list.Add(b);
			ret.ImpulseAxisActions.Add(b.Action);
		}

		return ret;
	}

	private static void addTrackedSources(Lookup lookup, InputStateAxisSource source) {
		if (source.Kind == InputStateAxisSourceKind.DigitalPair) {
			DigitalAxisSource d = source.DigitalValue;
			lookup.TrackedButtonSources.Add(d.Negative);
			lookup.TrackedButtonSources.Add(d.Positive);
		}
	}

	private static void addTrackedSources(Lookup lookup, InputStateAxis2dSource source) {
		switch (source.Kind.Tag) {
		case InputStateAxis2dSourceKind.Case.DigitalButtons:
			DigitalAxis2dSource d = source.DigitalValue;
			lookup.TrackedButtonSources.Add(d.Left);
			lookup.TrackedButtonSources.Add(d.Right);
			lookup.TrackedButtonSources.Add(d.Up);
			lookup.TrackedButtonSources.Add(d.Down);
			break;
		case InputStateAxis2dSourceKind.Case.Pair:
			StateAxis2dPairSource p = source.PairValue;
			addTrackedSources(lookup, p.X);
			addTrackedSources(lookup, p.Y);
			break;
		}
	}

	private void processInputEvent(InputEvent ev) {
		switch (ev) {
		case KeyEvent key:
			handleButtonSourceEdge(key.Tick, InputButtonSource.Key(key.Key), key.Edge, ButtonActionEventInfo.FromKey(key.Window));
			break;
		case GamepadButtonEvent gp:
			handleGamepadButton(gp.Tick, gp.Gamepad, gp.Button, gp.Edge == EdgeType.Press);
			break;
		case GamepadRemovedEvent removed:
			// normally the input source has already released everything the gamepad held
			foreach ((GamepadButton button, HashSet<GamepadId> holders) in gamepadButtonHolders)
				if (holders.Contains(removed.Gamepad))
					handleGamepadButton(removed.Tick, removed.Gamepad, button, down: false);
			break;
		case PointerMoveEvent move:
			events.Add(new PointerMoveControlEvent(move.Tick, move.Window, move.X, move.Y, new Vector2(move.DeltaX, move.DeltaY)));
			break;
		case PointerButtonEvent ptr:
			handleButtonSourceEdge(ptr.Tick, InputButtonSource.PointerButton(ptr.Button), ptr.Edge, ButtonActionEventInfo.FromPointer(ptr.Window, ptr.X, ptr.Y, ptr.Clicks));
			break;
		case PointerWheelEvent wheel:
			handlePointerWheel(wheel);
			break;
		case TextEnteredEvent text:
			events.Add(new TextEnteredControlEvent(text.Tick, text.Window, text.Text));
			break;
		}
	}

	/// <summary>
	/// Tracks which gamepads hold <paramref name="button"/> and forwards edges of the corresponding
	/// button source.
	/// </summary>
	/// <remarks>
	/// The source is down while any gamepad holds the button, so only the first press and the last
	/// release are edges.
	/// </remarks>
	private void handleGamepadButton(HostTick tick, GamepadId gamepad, GamepadButton button, bool down) {
		HashSet<GamepadId> holders = getHolders(button);
		bool wasHeld = holders.Count != 0;
		if (down)
			holders.Add(gamepad);
		else
			holders.Remove(gamepad);
		bool isHeld = holders.Count != 0;
		if (wasHeld != isHeld)
			handleButtonSourceEdge(tick, InputButtonSource.GamepadButton(button), isHeld ? EdgeType.Press : EdgeType.Release, ButtonActionEventInfo.None);
	}

	private HashSet<GamepadId> getHolders(GamepadButton button) {
		if (!gamepadButtonHolders.TryGetValue(button, out HashSet<GamepadId>? holders)) {
			holders = new HashSet<GamepadId>();
			gamepadButtonHolders.Add(button, holders);
		}
		return holders;
	}

	private void handleButtonSourceEdge(HostTick tick, InputButtonSource source, EdgeType edge, ButtonActionEventInfo info) {
		if (!lookup.TrackedButtonSources.Contains(source))
			return;

		bool wasDown = buttonSourceDown.TryGetValue(source, out bool old) && old;
		bool nowDown = edge == EdgeType.Press;
		if (wasDown == nowDown)
			return;

		buttonSourceDown[source] = nowDown;
		if (nowDown)
			buttonSourcePressedAt[source] = nextPressStamp++;
		else
			buttonSourcePressedAt.Remove(source);

		if (!lookup.ButtonActionsBySource.TryGetValue(source, out List<ActionId>? actions))
			return;

		foreach (ActionId action in actions) {
			int held = buttonHeldCounts.TryGetValue(action, out int h) ? h : 0;
			if (nowDown) {
				// multibind: press fires for every newly pressed bound source
				buttonHeldCounts[action] = held + 1;
				events.Add(new ButtonActionEvent(tick, action, EdgeType.Press, info));
			} else {
				// multibind: release only fires when all bound sources are up
				int next = Math.Max(held - 1, 0);
				buttonHeldCounts[action] = next;
				if (held > 0 && next == 0)
					events.Add(new ButtonActionEvent(tick, action, EdgeType.Release, info));
			}
		}
	}

	private void handlePointerWheel(PointerWheelEvent wheel) {
		handleImpulseAxis(
			wheel.Tick,
			InputImpulseAxisSource.PointerWheel(PointerWheelAxis.X),
			wheel.X,
			ImpulseAxisActionEventInfo.FromPointer(wheel.Window, wheel.PointerX, wheel.PointerY, wheel.IntegerX)
		);
		handleImpulseAxis(
			wheel.Tick,
			InputImpulseAxisSource.PointerWheel(PointerWheelAxis.Y),
			wheel.Y,
			ImpulseAxisActionEventInfo.FromPointer(wheel.Window, wheel.PointerX, wheel.PointerY, wheel.IntegerY)
		);
	}

	private void handleImpulseAxis(HostTick tick, InputImpulseAxisSource source, float amount, ImpulseAxisActionEventInfo info) {
		if (amount == 0f)
			return;
		if (!lookup.ImpulseAxesBySource.TryGetValue(source, out List<ImpulseAxisBinding>? bindings))
			return;
		foreach (ImpulseAxisBinding b in bindings) {
			float scaled = amount * b.Scale;
			if (scaled == 0f)
				continue;

			float cur = stepImpulseAmounts.TryGetValue(b.Action, out float old) ? old : 0f;
			stepImpulseAmounts[b.Action] = cur + scaled;
			events.Add(new ImpulseAxisActionEvent(tick, b.Action, scaled, info));
		}
	}

	private void evaluateButtonsFinal() {
		HashSet<ActionId> actions = new(lookup.ButtonActions);
		foreach (ActionId action in previousButtonDown.Keys)
			actions.Add(action);

		foreach (ActionId action in actions) {
			bool previous = previousButtonDown.TryGetValue(action, out bool p) && p;
			bool down = buttonHeldCounts.TryGetValue(action, out int held) && held > 0;
			buttonStates[action] = new ButtonActionState(down, previous);
			if (down)
				previousButtonDown[action] = true;
			else
				previousButtonDown.Remove(action);
		}
	}

	private void evaluateStateAxesFinal(HostTick tick, InputSnapshot raw) {
		Dictionary<ActionId, float> nextValues = new();

		foreach (StateAxisBinding b in map.StateAxisBindings) {
			float val = Math.Clamp(b.Deadzone.Apply(getStateAxisValue(raw, b.Source)) * b.Scale, -1f, 1f);
			nextValues.TryGetValue(b.Action, out float current);
			nextValues[b.Action] = mergeStateAxis(current, val, map.StateAxisMergePolicy);
		}

		HashSet<ActionId> actions = new(lookup.StateAxisActions);
		foreach (ActionId action in stateAxisValues.Keys)
			actions.Add(action);

		foreach (ActionId action in actions) {
			float old = stateAxisValues.TryGetValue(action, out float o) ? o : 0f;
			float next = nextValues.TryGetValue(action, out float n) ? n : 0f;
			stateAxisStates[action] = new StateAxisActionState(next, old);

			if (forceEmitStateAxes || old != next)
				events.Add(new StateAxisActionEvent(tick, action, next));

			if (next != 0f)
				stateAxisValues[action] = next;
			else
				stateAxisValues.Remove(action);
		}
	}

	private void evaluateStateAxes2dFinal(HostTick tick, InputSnapshot raw) {
		Dictionary<ActionId, Vector2> nextValues = new();

		foreach (StateAxis2dBinding b in map.StateAxis2dBindings) {
			Vector2 v = getStateAxis2dValue(raw, b.Source);
			v = b.Deadzone.Apply(v);
			v *= b.Scale;
			v = clampMag1(v);

			nextValues.TryGetValue(b.Action, out Vector2 current);
			nextValues[b.Action] = mergeStateAxis2d(current, v, map.StateAxis2dMergePolicy);
		}

		HashSet<ActionId> actions = new(lookup.StateAxis2dActions);
		foreach (ActionId action in stateAxis2dValues.Keys)
			actions.Add(action);

		foreach (ActionId action in actions) {
			stateAxis2dValues.TryGetValue(action, out Vector2 old);
			nextValues.TryGetValue(action, out Vector2 next);

			stateAxis2dStates[action] = new StateAxis2dActionState(next, old);
			if (forceEmitStateAxes2d || !nearlyEqual(old, next))
				events.Add(new StateAxis2dActionEvent(tick, action, next));

			if (next != Vector2.Zero)
				stateAxis2dValues[action] = next;
			else
				stateAxis2dValues.Remove(action);
		}
	}

	private void evaluateImpulseAxesFinal() {
		foreach ((ActionId action, float amount) in stepImpulseAmounts)
			if (amount != 0f)
				impulseAxisStates[action] = new ImpulseAxisActionState(amount);
	}

	private float getStateAxisValue(InputSnapshot raw, InputStateAxisSource source) {
		return source.Kind.Tag switch {
			InputStateAxisSourceKind.Case.GamepadAxis => getAnyGamepadAxis(raw.Gamepads, source.GamepadAxisValue),
			InputStateAxisSourceKind.Case.DigitalPair => getDigitalAxisValue(source.DigitalValue),
			_ => throw new UnreachableException(),
		};
	}

	private Vector2 getStateAxis2dValue(InputSnapshot raw, InputStateAxis2dSource source) {
		return source.Kind.Tag switch {
			InputStateAxis2dSourceKind.Case.GamepadStick => getAnyGamepadStick(raw.Gamepads, source.GamepadStickValue),
			InputStateAxis2dSourceKind.Case.DigitalButtons => getDigital2d(source.DigitalValue),
			InputStateAxis2dSourceKind.Case.Pair => getPair2d(raw, source.PairValue),
			_ => throw new UnreachableException(),
		};
	}

	private Vector2 getPair2d(InputSnapshot raw, StateAxis2dPairSource source) => new(
		getStateAxisValue(raw, source.X),
		getStateAxisValue(raw, source.Y)
	);

	private Vector2 getDigital2d(DigitalAxis2dSource source) {
		float x = getDigitalAxisValue(source.Left, source.Right, source.XSocd);
		float y = getDigitalAxisValue(source.Up, source.Down, source.YSocd);
		return clampMag1(new Vector2(x, y));
	}

	private float getDigitalAxisValue(DigitalAxisSource source) => getDigitalAxisValue(source.Negative, source.Positive, source.Socd);
	private float getDigitalAxisValue(InputButtonSource negative, InputButtonSource positive, SocdPolicy socd) {
		bool neg = buttonSourceDown.TryGetValue(negative, out bool n) && n;
		bool pos = buttonSourceDown.TryGetValue(positive, out bool p) && p;

		if (neg && !pos)
			return -1.0f;
		if (pos && !neg)
			return 1.0f;
		if (!neg && !pos)
			return 0.0f;

		return socd.Tag switch {
			SocdPolicy.Case.Last => socdLast(negative, positive),
			SocdPolicy.Case.First => socdFirst(negative, positive),
			SocdPolicy.Case.Neutral => 0f,
			SocdPolicy.Case.Positive => 1f,
			SocdPolicy.Case.Negative => -1f,
			_ => throw new UnreachableException(),
		};
	}

	private float socdLast(InputButtonSource negative, InputButtonSource positive) {
		ulong negAt = buttonSourcePressedAt.TryGetValue(negative, out ulong n) ? n : throw new InternalStateException("negative source isn't down");
		ulong posAt = buttonSourcePressedAt.TryGetValue(positive, out ulong p) ? p : throw new InternalStateException("positive source isn't down");
		if (negAt == posAt)
			throw new InternalStateException(
				"negative/positive sources ended up with the same stamp; either it's not getting incremented correctly or both directions somehow resolved to the same source"
			);
		return negAt > posAt ? -1f : 1f;
	}

	private float socdFirst(InputButtonSource negative, InputButtonSource positive) {
		ulong negAt = buttonSourcePressedAt.TryGetValue(negative, out ulong n) ? n : throw new InternalStateException("negative source isn't down");
		ulong posAt = buttonSourcePressedAt.TryGetValue(positive, out ulong p) ? p : throw new InternalStateException("positive source isn't down");
		if (negAt == posAt)
			throw new InternalStateException(
				"negative/positive sources ended up with the same stamp; either it's not getting incremented correctly or both directions somehow resolved to the same source"
			);
		return negAt < posAt ? -1f : 1f;
	}

	private static bool isButtonSourceDown(InputSnapshot raw, InputButtonSource source) {
		return source.Kind.Tag switch {
			InputButtonSourceKind.Case.Key => raw.Keyboard.IsDown(source.KeyValue),
			InputButtonSourceKind.Case.PointerButton => raw.Pointer.IsDown(source.PointerButtonValue),
			InputButtonSourceKind.Case.GamepadButton => anyGamepadButtonDown(raw.Gamepads, source.GamepadButtonValue),
			_ => throw new UnreachableException(),
		};
	}

	private static bool anyGamepadButtonDown(GamepadStateSet gamepads, GamepadButton button) {
		foreach (GamepadStateEntry ent in gamepads)
			if (ent.State.IsDown(button))
				return true;
		return false;
	}

	private static float getAnyGamepadAxis(GamepadStateSet gamepads, GamepadAxis axis) {
		float max = 0.0f;
		foreach (GamepadStateEntry ent in gamepads) {
			float v = ent.State.GetAxis(axis);
			if (MathF.Abs(v) > MathF.Abs(max))
				max = v;
		}
		return max;
	}

	private static Vector2 getAnyGamepadStick(GamepadStateSet gamepads, GamepadStick stick) {
		GamepadAxis xAxis = stick == GamepadStick.Left ? GamepadAxis.LeftX : GamepadAxis.RightX;
		GamepadAxis yAxis = stick == GamepadStick.Left ? GamepadAxis.LeftY : GamepadAxis.RightY;

		Vector2 max = Vector2.Zero;
		foreach (GamepadStateEntry ent in gamepads) {
			Vector2 v = new(ent.State.GetAxis(xAxis), ent.State.GetAxis(yAxis));
			if (v.LengthSquared() > max.LengthSquared())
				max = v;
		}
		return max;
	}

	private static float mergeStateAxis(float a, float b, StateAxisMergePolicy policy) {
		return policy.Tag switch {
			StateAxisMergePolicy.Case.MaxAbs => MathF.Abs(b) > MathF.Abs(a) ? b : a,
			StateAxisMergePolicy.Case.SumClamp => Math.Clamp(a + b, -1f, 1f),
			_ => throw new UnreachableException(),
		};
	}

	private static Vector2 mergeStateAxis2d(Vector2 a, Vector2 b, StateAxis2dMergePolicy policy) {
		return policy.Tag switch {
			StateAxis2dMergePolicy.Case.MaxMagnitude => b.LengthSquared() > a.LengthSquared() ? b : a,
			StateAxis2dMergePolicy.Case.SumClamp => clampMag1(a + b),
			_ => throw new UnreachableException(),
		};
	}

	private static Vector2 clampMag1(Vector2 v) {
		float lenSq = v.LengthSquared();
		if (lenSq <= 1f)
			return v;
		return v / MathF.Sqrt(lenSq);
	}

	private static bool nearlyEqual(Vector2 a, Vector2 b, float epsilon = 1e-4f) =>
		MathF.Abs(a.X - b.X) <= epsilon && MathF.Abs(a.Y - b.Y) <= epsilon;
}
