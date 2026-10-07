// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Injure.Time;

/// <summary>
/// Represents a position on a timeline-like domain.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be value types with well-defined equivalence-relation equality and total
/// ordering.
/// </para>
/// <para>
/// This interface only covers ordering; see <see cref="ITimelineInstant{TSelf, TDur}"/> for
/// instants that support arithmetic with a duration type.
/// </para>
/// </remarks>
public interface ITimelineInstant<TSelf>
	: IEquatable<TSelf>,
	IComparable<TSelf>, IComparable, IComparisonOperators<TSelf, TSelf, bool>,
	ISpanFormattable
	where TSelf : struct, ITimelineInstant<TSelf>;

/// <summary>
/// Represents a position on a timeline-like domain, with an associated duration type.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be value types with well-defined equivalence-relation equality and total
/// ordering, and satisfy <c>a + (b - a) == b</c> whenever neither operation overflows.
/// </para>
/// <para>
/// Instants deliberately have no addition with each other and no domain-wide zero; only the
/// difference between two instants of the same domain is meaningful.
/// </para>
/// </remarks>
public interface ITimelineInstant<TSelf, TDur>
	: ITimelineInstant<TSelf>,
	IAdditionOperators<TSelf, TDur, TSelf>, ISubtractionOperators<TSelf, TDur, TSelf>
	where TSelf : struct, ITimelineInstant<TSelf, TDur>
	where TDur : struct, ITimelineDuration<TDur> {
	// declared directly since ISubtractionOperators<TSelf, TSelf, TDur> can't be listed next to
	// ISubtractionOperators<TSelf, TDur, TSelf> (CS0695, they unify if TDur == TSelf)
	/// <summary>
	/// Returns the duration from <paramref name="right"/> to <paramref name="left"/>.
	/// </summary>
	static abstract TDur operator -(TSelf left, TSelf right);
}

/// <summary>
/// Represents a length of time in a timeline-like domain.
/// </summary>
/// <remarks>
/// Implementations must be value types with well-defined equivalence-relation equality, total
/// ordering, and a domain-defined additive zero.
/// </remarks>
public interface ITimelineDuration<TSelf>
	: IEquatable<TSelf>,
	IComparable<TSelf>, IComparable, IComparisonOperators<TSelf, TSelf, bool>,
	IAdditionOperators<TSelf, TSelf, TSelf>, ISubtractionOperators<TSelf, TSelf, TSelf>,
	ISpanFormattable
	where TSelf : struct, ITimelineDuration<TSelf> {
	/// <summary>
	/// The zero-length duration, as well as the additive identity for this type.
	/// </summary>
	static abstract TSelf Zero { get; }
}

/// <summary>
/// Represents a length of time in a timeline-like domain with a fixed linear mapping to elapsed
/// real time.
/// </summary>
/// <remarks>
/// <para>
/// Equal durations must represent equal amounts of elapsed real time throughout the domain.
/// </para>
/// <para>
/// This models monotonic real elapsed time (as opposed to e.g CPU time) subject to
/// measurement/scheduling inaccuracies, not calendar/timezone/NTP/civil-time semantics.
/// </para>
/// </remarks>
public interface IRealTimeDuration<TSelf> : ITimelineDuration<TSelf> where TSelf : struct, IRealTimeDuration<TSelf> {
	/// <summary>
	/// Converts this duration to seconds, subject to precision loss from conversion/rounding.
	/// </summary>
	double ToSeconds();

	/// <summary>
	/// Returns the duration that is <paramref name="sec"/> seconds long, subject to precision
	/// loss from conversion/rounding.
	/// </summary>
	static abstract TSelf FromSeconds(double sec);
}
