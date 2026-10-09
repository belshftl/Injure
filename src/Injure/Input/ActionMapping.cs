// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Injure.Input;

/// <summary>
/// An immutable set of action bindings, plus the policies for merging several bindings of the same
/// action.
/// </summary>
/// <remarks>
/// Usually built with an <see cref="ActionMapBuilder"/> and published through an
/// <see cref="ActionProfile"/>.
/// </remarks>
public sealed class ActionMapSnapshot {
	private readonly ImmutableArray<ButtonBinding> buttonBindings;
	private readonly ImmutableArray<StateAxisBinding> stateAxisBindings;
	private readonly ImmutableArray<StateAxis2dBinding> stateAxis2dBindings;
	private readonly ImmutableArray<ImpulseAxisBinding> impulseAxisBindings;

	/// <summary>Button bindings.</summary>
	public IReadOnlyList<ButtonBinding> ButtonBindings => buttonBindings;

	/// <summary>1D state axis bindings.</summary>
	public IReadOnlyList<StateAxisBinding> StateAxisBindings => stateAxisBindings;

	/// <summary>2D state axis bindings.</summary>
	public IReadOnlyList<StateAxis2dBinding> StateAxis2dBindings => stateAxis2dBindings;

	/// <summary>Impulse axis bindings.</summary>
	public IReadOnlyList<ImpulseAxisBinding> ImpulseAxisBindings => impulseAxisBindings;

	/// <summary>
	/// How several bindings of the same 1D state axis action are combined.
	/// </summary>
	public StateAxisMergePolicy StateAxisMergePolicy { get; }

	/// <summary>
	/// How several bindings of the same 2D state axis action are combined.
	/// </summary>
	public StateAxis2dMergePolicy StateAxis2dMergePolicy { get; }

	/// <summary>
	/// Creates a map from copies of the given bindings.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if a binding list binds the same action to the same input more than once.
	/// </exception>
	public ActionMapSnapshot(
		ReadOnlySpan<ButtonBinding> buttonBindings,
		ReadOnlySpan<StateAxisBinding> stateAxisBindings,
		ReadOnlySpan<StateAxis2dBinding> stateAxis2dBindings,
		ReadOnlySpan<ImpulseAxisBinding> impulseAxisBindings,
		StateAxisMergePolicy stateAxisMergePolicy,
		StateAxis2dMergePolicy stateAxis2dMergePolicy
	) {
		this.buttonBindings = validate(buttonBindings, b => (b.Action, b.Source), "button");
		this.stateAxisBindings = validate(stateAxisBindings, b => (b.Action, b.Source), "state axis");
		this.stateAxis2dBindings = validate(stateAxis2dBindings, b => (b.Action, b.Source), "2D state axis");
		this.impulseAxisBindings = validate(impulseAxisBindings, b => (b.Action, b.Source), "impulse axis");
		StateAxisMergePolicy = stateAxisMergePolicy;
		StateAxis2dMergePolicy = stateAxis2dMergePolicy;
	}

	private static ImmutableArray<TBinding> validate<TBinding, TSource>(
		ReadOnlySpan<TBinding> bindings,
		Func<TBinding, (ActionId, TSource)> getData,
		string kind
	) where TBinding : struct where TSource : struct {
		HashSet<(ActionId, TSource)> seen = new();
		foreach (TBinding b in bindings)
			if (!seen.Add(getData(b)))
				throw new ArgumentException($"{kind} bindings must not contain duplicate action/source pairs");
		return bindings.ToImmutableArray();
	}
}

/// <summary>
/// A mutable builder for <see cref="ActionMapSnapshot"/>s.
/// </summary>
/// <remarks>
/// The merge policies default to <see cref="StateAxisMergePolicy.MaxAbs"/> and
/// <see cref="StateAxis2dMergePolicy.MaxMagnitude"/>.
/// </remarks>
public sealed class ActionMapBuilder {
	private readonly List<ButtonBinding> buttonBindings = new();
	private readonly List<StateAxisBinding> stateAxisBindings = new();
	private readonly List<StateAxis2dBinding> stateAxis2dBindings = new();
	private readonly List<ImpulseAxisBinding> impulseAxisBindings = new();

	/// <summary>Button bindings added so far.</summary>
	public IReadOnlyList<ButtonBinding> ButtonBindings => buttonBindings;

	/// <summary>1D state axis bindings added so far.</summary>
	public IReadOnlyList<StateAxisBinding> StateAxisBindings => stateAxisBindings;

	/// <summary>2D state axis bindings added so far.</summary>
	public IReadOnlyList<StateAxis2dBinding> StateAxis2dBindings => stateAxis2dBindings;

	/// <summary>Impulse axis bindings added so far.</summary>
	public IReadOnlyList<ImpulseAxisBinding> ImpulseAxisBindings => impulseAxisBindings;

	/// <summary>
	/// How several bindings of the same 1D state axis action are combined.
	/// </summary>
	public StateAxisMergePolicy StateAxisMergePolicy { get; set; } = StateAxisMergePolicy.MaxAbs;

	/// <summary>
	/// How several bindings of the same 2D state axis action are combined.
	/// </summary>
	public StateAxis2dMergePolicy StateAxis2dMergePolicy { get; set; } = StateAxis2dMergePolicy.MaxMagnitude;

	/// <summary>
	/// Creates a builder that starts out with the contents of <paramref name="snapshot"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	public static ActionMapBuilder FromSnapshot(ActionMapSnapshot snapshot) {
		ArgumentNullException.ThrowIfNull(snapshot);
		ActionMapBuilder b = new() {
			StateAxisMergePolicy = snapshot.StateAxisMergePolicy,
			StateAxis2dMergePolicy = snapshot.StateAxis2dMergePolicy,
		};
		b.buttonBindings.AddRange(snapshot.ButtonBindings);
		b.stateAxisBindings.AddRange(snapshot.StateAxisBindings);
		b.impulseAxisBindings.AddRange(snapshot.ImpulseAxisBindings);
		b.stateAxis2dBindings.AddRange(snapshot.StateAxis2dBindings);
		return b;
	}

	/// <summary>
	/// Adds a <see cref="ButtonBinding"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="action"/> is invalid or already bound to <paramref name="source"/>.
	/// </exception>
	public void BindButton(ActionId action, InputButtonSource source) {
		ensureValid(action);
		foreach (ButtonBinding b in buttonBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate button binding");
		buttonBindings.Add(new ButtonBinding(action, source));
	}

	/// <summary>
	/// Adds a <see cref="StateAxisBinding"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="action"/> is invalid or already bound to <paramref name="source"/>.
	/// </exception>
	public void BindStateAxis(ActionId action, InputStateAxisSource source, AxisDeadzone deadzone, float scale = 1f) {
		ensureValid(action);
		foreach (StateAxisBinding b in stateAxisBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate state axis binding");
		stateAxisBindings.Add(new StateAxisBinding(action, source, deadzone, scale));
	}

	/// <summary>
	/// Adds a <see cref="StateAxis2dBinding"/> with a scale of (1, 1).
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="action"/> is invalid or already bound to <paramref name="source"/>.
	/// </exception>
	public void BindStateAxis2d(ActionId action, InputStateAxis2dSource source, Axis2dDeadzone deadzone) {
		ensureValid(action);
		foreach (StateAxis2dBinding b in stateAxis2dBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate 2D state axis binding");
		stateAxis2dBindings.Add(new StateAxis2dBinding(action, source, deadzone, Vector2.One));
	}

	/// <summary>
	/// Adds a <see cref="StateAxis2dBinding"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="action"/> is invalid or already bound to <paramref name="source"/>.
	/// </exception>
	public void BindStateAxis2d(ActionId action, InputStateAxis2dSource source, Axis2dDeadzone deadzone, Vector2 scale) {
		ensureValid(action);
		foreach (StateAxis2dBinding b in stateAxis2dBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate 2D state axis binding");
		stateAxis2dBindings.Add(new StateAxis2dBinding(action, source, deadzone, scale));
	}

	/// <summary>
	/// Adds an <see cref="ImpulseAxisBinding"/>.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="action"/> is invalid or already bound to <paramref name="source"/>.
	/// </exception>
	public void BindImpulseAxis(ActionId action, InputImpulseAxisSource source, float scale = 1f) {
		ensureValid(action);
		foreach (ImpulseAxisBinding b in impulseAxisBindings)
			if (b.Action == action && b.Source == source)
				throw new ArgumentException("duplicate impulse axis binding");
		impulseAxisBindings.Add(new ImpulseAxisBinding(action, source, scale));
	}

	/// <summary>
	/// Removes every binding of <paramref name="action"/>.
	/// </summary>
	public void ClearBindingsFor(ActionId action) {
		buttonBindings.RemoveAll(b => b.Action == action);
		stateAxisBindings.RemoveAll(b => b.Action == action);
		impulseAxisBindings.RemoveAll(b => b.Action == action);
		stateAxis2dBindings.RemoveAll(b => b.Action == action);
	}

	/// <summary>
	/// Removes every button binding of <paramref name="action"/>.
	/// </summary>
	public void ClearButtonBindings(ActionId action) => buttonBindings.RemoveAll(b => b.Action == action);

	/// <summary>
	/// Removes every 1D state axis binding of <paramref name="action"/>.
	/// </summary>
	public void ClearStateAxisBindings(ActionId action) => stateAxisBindings.RemoveAll(b => b.Action == action);

	/// <summary>
	/// Removes every impulse axis binding of <paramref name="action"/>.
	/// </summary>
	public void ClearImpulseAxisBindings(ActionId action) => impulseAxisBindings.RemoveAll(b => b.Action == action);

	/// <summary>
	/// Removes every 2D state axis binding of <paramref name="action"/>.
	/// </summary>
	public void ClearStateAxis2dBindings(ActionId action) => stateAxis2dBindings.RemoveAll(b => b.Action == action);

	/// <summary>
	/// Removes every binding. The merge policies are kept.
	/// </summary>
	public void Clear() {
		buttonBindings.Clear();
		stateAxisBindings.Clear();
		stateAxis2dBindings.Clear();
		impulseAxisBindings.Clear();
	}

	/// <summary>
	/// Creates an immutable snapshot of the current contents.
	/// </summary>
	public ActionMapSnapshot ToSnapshot() => new(
		CollectionsMarshal.AsSpan(buttonBindings),
		CollectionsMarshal.AsSpan(stateAxisBindings),
		CollectionsMarshal.AsSpan(stateAxis2dBindings),
		CollectionsMarshal.AsSpan(impulseAxisBindings),
		StateAxisMergePolicy,
		StateAxis2dMergePolicy
	);

	private static void ensureValid(ActionId action) {
		if (!action.IsValid)
			throw new ArgumentException("action ID must be valid", nameof(action));
	}
}

/// <summary>
/// A replaceable reference to the current <see cref="ActionMapSnapshot"/>, e.g. a player's control
/// settings, shared by the <see cref="ActionCtx"/>s that evaluate it.
/// </summary>
/// <param name="initial">The initial map.</param>
/// <exception cref="ArgumentNullException">
/// Thrown if <paramref name="initial"/> is <see langword="null"/>.
/// </exception>
/// <remarks>
/// Thread-safe. Contexts pick up a replaced map on their next
/// <see cref="ActionCtx.Update(Host.HostTick, in InputView)"/>.
/// </remarks>
public sealed class ActionProfile(ActionMapSnapshot initial) {
	private ActionMapSnapshot current = initial ?? throw new ArgumentNullException(nameof(initial));
	private ulong version = 1;

	/// <summary>The current map.</summary>
	public ActionMapSnapshot Current => Volatile.Read(ref current);

	/// <summary>
	/// Incremented on every <see cref="Replace(ActionMapSnapshot)"/>; starts at 1.
	/// </summary>
	public ulong Version => Volatile.Read(ref version);

	/// <summary>
	/// Replaces the current map.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="next"/> is <see langword="null"/>.
	/// </exception>
	public void Replace(ActionMapSnapshot next) {
		ArgumentNullException.ThrowIfNull(next);
		Volatile.Write(ref current, next);
		Interlocked.Increment(ref version);
	}
}
