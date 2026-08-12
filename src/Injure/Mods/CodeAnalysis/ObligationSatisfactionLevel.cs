// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Mods.CodeAnalysis;

// NOTE: this enum's numeric values are mirrored in Injure.Mods.Analyzers/Lifetime/ObligationEnums.cs
public enum ObligationSatisfactionLevel {
	Generation = 1,
	Method = 2,
}
