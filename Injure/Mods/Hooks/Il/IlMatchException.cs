// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Injure.Mods.Hooks.Il;

/// <summary>
/// Exception thrown when a required IL pattern was not matched with the requested cardinality
/// and/or search direction.
/// </summary>
public sealed class IlMatchException : IlPipelineException {
	private IlMatchException(string message) : base(message) {}
	internal static IlMatchException ExpectedAny(in IlPatternSearchInfo info) => new(
		$"""
		expected to match IL pattern:
		{info.PatternDisplay}
		searching {info.Direction.Display()} from instruction boundary {info.StartInstructionBoundary.ToString(CultureInfo.InvariantCulture)}, didn't match anything
		"""
	);
	internal static IlMatchException ExpectedOne(in IlPatternSearchInfo info, int matchedCount) => new(
		$"""
		expected to match exactly 1 occurrence of IL pattern:
		{info.PatternDisplay}
		searching {info.Direction.Display()} from instruction boundary {info.StartInstructionBoundary.ToString(CultureInfo.InvariantCulture)}, matched {matchedCount} occurrences
		"""
	);
}
