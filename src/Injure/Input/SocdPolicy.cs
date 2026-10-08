// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Input;

/// <summary>
/// How a digital axis resolves both of its directions being held at once (SOCD, "simultaneous
/// opposing cardinal directions").
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct SocdPolicy {
	/// <summary>Raw switch tag for <see cref="SocdPolicy"/>.</summary>
	public enum Case {
		/// <summary>
		/// The most recently pressed direction wins.
		/// </summary>
		/// <remarks>
		/// Recommended for most cases; other SOCD policies tend to feel much less responsive, and/or
		/// like the decided input is arbitrary.
		/// </remarks>
		Last = 1,

		/// <summary>
		/// The direction that has been held the longest wins.
		/// </summary>
		First,

		/// <summary>
		/// The axis reads 0.
		/// </summary>
		Neutral,

		/// <summary>
		/// The axis reads +1.
		/// </summary>
		Positive,

		/// <summary>
		/// The axis reads -1.
		/// </summary>
		Negative,
	}
}
