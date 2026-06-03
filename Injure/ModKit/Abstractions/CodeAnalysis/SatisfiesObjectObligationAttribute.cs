// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions.CodeAnalysis;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SatisfiesObjectObligationAttribute : Attribute {
	public SatisfiesObjectObligationAttribute(ObligationSatisfactionLevel level) {
		if (!(level is ObligationSatisfactionLevel.Generation or ObligationSatisfactionLevel.Method))
			throw new ArgumentOutOfRangeException(nameof(level), level, "out of range ObligationSatisfactionLevel enum value");
		Level = level;
	}

	public ObligationSatisfactionLevel Level { get; }
}
