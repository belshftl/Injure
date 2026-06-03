// SPDX-License-Identifier: MIT

namespace Injure.ModKit.Abstractions.CodeAnalysis;

// NOTE: this enum's numeric values are mirrored in Injure.ModKit.Analyzers/Lifetime/ObligationEnums.cs
public enum ObligationSatisfactionLevel {
	Generation = 1,
	Method = 2,
}
