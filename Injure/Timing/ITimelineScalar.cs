// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System;
using System.Numerics;

namespace Injure.Timing;

/// <summary>
/// Represents an ordered additive scalar in a timeline-like domain.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be value types with well-defined equivalence-relation equality,
/// total ordering, and a domain-defined additive zero.
/// </para>
/// <para>
/// Values are domain-specific and conversion to real / wall-clock time or units in another
/// domain may require external context.
/// </para>
/// </remarks>
public interface ITimelineScalar<TSelf> : IEquatable<TSelf>, IComparable<TSelf>, IComparable,
	IComparisonOperators<TSelf, TSelf, bool>, IAdditionOperators<TSelf, TSelf, TSelf>, ISubtractionOperators<TSelf, TSelf, TSelf>,
	ISpanFormattable where TSelf : struct, ITimelineScalar<TSelf> {
	/// <summary>
	/// Domain-defined zero value, as well as the additive identity for this type.
	/// </summary>
	static abstract TSelf Zero { get; }
}

/// <summary>
/// Represents an ordered additive scalar in a timeline-like domain with a fixed affine
/// mapping to elapsed real time.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be value types with well-defined equivalence-relation equality,
/// total ordering, and an additive zero (corresponding to zero seconds), as well as have
/// a stable affine mapping between values and seconds, where equal value deltas represent
/// equal durations of elapsed real time throughout the domain.
/// </para>
/// <para>
/// This models monotonic real elapsed time (as opposed to e.g CPU time) subject to
/// measurement/scheduling inaccuracies, not calendar/timezone/NTP/civil-time semantics.
/// </para>
/// </remarks>
public interface IRealTimeScalar<TSelf> : ITimelineScalar<TSelf> where TSelf : struct, IRealTimeScalar<TSelf> {
	/// <summary>
	/// Converts the duration between <see cref="ITimelineScalar{TSelf}.Zero"/> and this position
	/// on the timeline to seconds, subject to precision loss from conversion/rounding.
	/// </summary>
	double ToSeconds();

	/// <summary>
	/// Returns a value for the position of the timeline <paramref name="sec"/> seconds after
	/// <see cref="ITimelineScalar{TSelf}.Zero"/>, subject to precision loss from conversion/rounding.
	/// </summary>
	static abstract TSelf FromSeconds(double sec);
}
