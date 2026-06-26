// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Globalization;

using Injure.Internals.Analyzers.Attributes;

namespace Injure.Mods.Utils.MonoMod;

/// <summary>
/// Represents a direction in which a search for an IL pattern or sequence was performed
/// or is to be performed.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ILPatternSearchDirection {
	/// <summary>Raw switch tag for <see cref="ILPatternSearchDirection"/>.</summary>
	public enum Case {
		/// <summary>
		/// The search was/is performed forwards from the cursor's location.
		/// </summary>
		Forward = 1,

		/// <summary>
		/// The search was/is performed backwards from the cursor's location.
		/// </summary>
		Backward,
	}
}

/// <summary>
/// Exception thrown when an IL hook's manipulator fails to locate or match a specific
/// pattern or sequence in the IL of the method being patched.
/// </summary>
public sealed class ILPatternNotFoundException(
	string target,
	string expected,
	int startIndex,
	ILPatternSearchDirection searchDirection
) : InvalidOperationException(fmt(target, expected, startIndex, searchDirection)) {
	/// <summary>
	/// Name of the target method, or a human-readable fallback string if one
	/// isn't available.
	/// </summary>
	public string Target { get; } = target;

	/// <summary>
	/// Short human-readable description of the expected IL pattern that was
	/// being searched for.
	/// </summary>
	public string Expected { get; } = expected;

	/// <summary>
	/// IL cursor index at which the search begun.
	/// </summary>
	public int StartIndex { get; } = startIndex;

	/// <summary>
	/// Direction from <see cref="StartIndex"/> in which the search was performed.
	/// </summary>
	public ILPatternSearchDirection SearchDirection { get; } = searchDirection;

	private static string fmt(string target, string expected, int startIndex, ILPatternSearchDirection searchDirection) {
		string dir = searchDirection.Tag switch {
			ILPatternSearchDirection.Case.Forward => "forward",
			ILPatternSearchDirection.Case.Backward => "backward",
			_ => throw new UnreachableException(),
		};
		return $"expected to match IL pattern '{expected}' in method '{target}' searching {dir} from instruction index {startIndex.ToString(CultureInfo.InvariantCulture)}";
	}
}
