// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Draw;

/// <summary>
/// How a line strip or loop is drawn at points where two segments meet.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Miter"/>.
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // the sharp corner is what outlines of shapes have too
public readonly partial struct LineJoin {
	/// <summary>Raw switch tag for <see cref="LineJoin"/>.</summary>
	public enum Case {
		/// <summary>
		/// Extends the outer edges of both segments until they meet at a sharp corner. If that corner
		/// would reach further than twice the thickness from the point (happens if the segments meet at
		/// less than roughly 29 degrees), acts like <see cref="Bevel"/> instead, as to not produce a
		/// ridiculously long miter.
		/// </summary>
		Miter,

		/// <summary>
		/// Cuts the corner off with a straight edge between the outer edges of both segments.
		/// </summary>
		Bevel,

		/// <summary>
		/// Rounds the corner off with a circular arc around the point, with a radius of half the
		/// thickness.
		/// </summary>
		Round,
	}
}

/// <summary>
/// How a line is drawn at its ends.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Butt"/>.
/// </remarks>
[ClosedEnum(CheckZeroName = false)] // ends exactly at the end points, adding nothing
public readonly partial struct LineCap {
	/// <summary>Raw switch tag for <see cref="LineCap"/>.</summary>
	public enum Case {
		/// <summary>
		/// Cuts the line off square right at the end point. In other words, no line cap at all.
		/// </summary>
		Butt,

		/// <summary>
		/// Cuts the line off square half the thickness past the end point.
		/// </summary>
		Square,

		/// <summary>
		/// Ends the line in a half-disc around the end point, with a radius of half the thickness.
		/// </summary>
		Round,
	}
}
