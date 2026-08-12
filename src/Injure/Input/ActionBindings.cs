// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using Injure.DevAnalyzers.Attributes;

namespace Injure.Input;

public readonly record struct ButtonBinding(
	ActionId Action,
	InputButtonSource Source
);

public readonly record struct StateAxisBinding(
	ActionId Action,
	InputStateAxisSource Source,
	AxisDeadzone Deadzone,
	float Scale
);

public readonly record struct StateAxis2DBinding(
	ActionId Action,
	InputStateAxis2DSource Source,
	Axis2DDeadzone Deadzone,
	Vector2 Scale
);

public readonly record struct ImpulseAxisBinding(
	ActionId Action,
	InputImpulseAxisSource Source,
	float Scale
);

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct StateAxisMergePolicy {
	public enum Case {
		MaxAbs = 1,
		SumClamp,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct StateAxis2DMergePolicy {
	public enum Case {
		MaxMagnitude = 1,
		SumClamp,
	}
}
