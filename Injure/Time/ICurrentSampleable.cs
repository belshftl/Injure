// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Time;

/// <summary>
/// Represents a source of <see cref="ITimelineScalar{TSelf}"/> values with the capability
/// to sample the current value, where "current" is domain-defined.
/// </summary>
/// <remarks>
/// Readings may not be monotonic; given two samples A and B, where B was sampled later in
/// time than A, the two may still order B &lt; A within the domain.
/// </remarks>
public interface ICurrentSampleable<out T> where T : struct, ITimelineScalar<T> {
	/// <summary>
	/// Samples the current value.
	/// </summary>
	/// <remarks>
	/// See this type's <c>&lt;remarks&gt;</c> for notes on monotonicity.
	/// </remarks>
	T SampleCurrent();
}

/// <summary>
/// Represents a source of <see cref="ITimelineScalar{TSelf}"/> values with the capability
/// to sample the current value, where "current" is domain-defined but monotonically non-decreasing.
/// </summary>
/// <remarks>
/// Readings are monotonic; given two samples A and B, where B was sampled later in time than
/// A, the two are guaranteed to order B == A or B &gt; A within the domain.
/// </remarks>
public interface IMonoCurrentSampleable<out T> : ICurrentSampleable<T> where T : struct, ITimelineScalar<T> {
}
