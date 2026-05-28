// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SatisfiesAndReturnsAttribute : Attribute {
	public SatisfiesAndReturnsAttribute(string parameterName) {
		ParameterName = parameterName;
		ParameterIndex = -1;
	}

	public SatisfiesAndReturnsAttribute(int parameterIndex) {
		ParameterIndex = parameterIndex;
		ParameterName = "";
	}

	public string ParameterName { get; }
	public int ParameterIndex { get; }
}
