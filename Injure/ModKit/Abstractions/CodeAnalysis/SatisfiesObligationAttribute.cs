// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SatisfiesObligationAttribute : Attribute {
	public SatisfiesObligationAttribute(string parameterName, ObligationSatisfactionLevel level) {
		ParameterName = parameterName;
		ParameterIndex = -1;
		Level = level;
	}

	public SatisfiesObligationAttribute(int parameterIndex, ObligationSatisfactionLevel level) {
		ParameterIndex = parameterIndex;
		ParameterName = "";
		Level = level;
	}

	public string ParameterName { get; }
	public int ParameterIndex { get; }
	public ObligationSatisfactionLevel Level { get; }
}
