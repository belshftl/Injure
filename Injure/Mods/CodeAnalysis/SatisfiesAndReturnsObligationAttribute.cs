// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;

namespace Injure.Mods.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SatisfiesAndReturnsObligationAttribute : Attribute {
	public SatisfiesAndReturnsObligationAttribute(string parameterName, ObligationSatisfactionLevel level) {
		ParameterName = parameterName;
		ParameterIndex = -1;
		if (!(level is ObligationSatisfactionLevel.Generation or ObligationSatisfactionLevel.Method))
			throw new ArgumentOutOfRangeException(nameof(level), level, "out of range ObligationSatisfactionLevel enum value");
		Level = level;
	}

	public SatisfiesAndReturnsObligationAttribute(int parameterIndex, ObligationSatisfactionLevel level) {
		ParameterIndex = parameterIndex;
		ParameterName = "";
		if (!(level is ObligationSatisfactionLevel.Generation or ObligationSatisfactionLevel.Method))
			throw new ArgumentOutOfRangeException(nameof(level), level, "out of range ObligationSatisfactionLevel enum value");
		Level = level;
	}

	public string ParameterName { get; }
	public int ParameterIndex { get; }
	public ObligationSatisfactionLevel Level { get; }
}
