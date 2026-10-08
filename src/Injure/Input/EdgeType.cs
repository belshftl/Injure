// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Input;

/// <summary>
/// Whether a button-like input went down or up.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct EdgeType {
	/// <summary>Raw switch tag for <see cref="EdgeType"/>.</summary>
	public enum Case {
		/// <summary>The input went down.</summary>
		Press = 1,

		/// <summary>The input went up.</summary>
		Release,
	}
}
