// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Input;

/// <summary>
/// Binds a button action to a button-like input: the action is held while any of its bound inputs
/// is held.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Action"/> is.
/// </remarks>
public readonly record struct ButtonBinding(
	ActionId Action,
	InputButtonSource Source
);

/// <summary>
/// Binds a 1D state axis action to an input: the binding's value is the input's value with
/// <see cref="Deadzone"/> applied, multiplied by <see cref="Scale"/>, and clamped to [-1, 1].
/// </summary>
/// <remarks>
/// <para>
/// Several bindings of the same action are merged according to the map's
/// <see cref="StateAxisMergePolicy"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <see cref="Action"/> is.
/// </para>
/// </remarks>
public readonly record struct StateAxisBinding(
	ActionId Action,
	InputStateAxisSource Source,
	AxisDeadzone Deadzone,
	float Scale
);

/// <summary>
/// Binds a 2D state axis action to an input: the binding's value is the input's value with
/// <see cref="Deadzone"/> applied, multiplied component-wise by <see cref="Scale"/>, and clamped to
/// a magnitude of at most 1.
/// </summary>
/// <remarks>
/// <para>
/// Several bindings of the same action are merged according to the map's
/// <see cref="StateAxis2dMergePolicy"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid, since its <see cref="Action"/> is.
/// </para>
/// </remarks>
public readonly record struct StateAxis2dBinding(
	ActionId Action,
	InputStateAxis2dSource Source,
	Axis2dDeadzone Deadzone,
	Vector2 Scale
);

/// <summary>
/// Binds an impulse axis action to an impulse input: each impulse adds its amount multiplied by
/// <see cref="Scale"/> to the action's amount for the step. Not clamped.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Action"/> is.
/// </remarks>
public readonly record struct ImpulseAxisBinding(
	ActionId Action,
	InputImpulseAxisSource Source,
	float Scale
);

/// <summary>
/// How the values of several bindings of the same 1D state axis action are combined.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct StateAxisMergePolicy {
	/// <summary>Raw switch tag for <see cref="StateAxisMergePolicy"/>.</summary>
	public enum Case {
		/// <summary>
		/// The value with the largest magnitude wins.
		/// </summary>
		MaxAbs = 1,

		/// <summary>
		/// The values are added up, and the sum is clamped to [-1, 1].
		/// </summary>
		SumClamp,
	}
}

/// <summary>
/// How the values of several bindings of the same 2D state axis action are combined.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct StateAxis2dMergePolicy {
	/// <summary>Raw switch tag for <see cref="StateAxis2dMergePolicy"/>.</summary>
	public enum Case {
		/// <summary>
		/// The value with the largest magnitude wins.
		/// </summary>
		MaxMagnitude = 1,

		/// <summary>
		/// The values are added up, and the sum is clamped to a magnitude of at most 1.
		/// </summary>
		SumClamp,
	}
}
