// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Host;

/// <summary>
/// A series of deadlines at a fixed rate, e.g. for render/loop cadence, with a configurable
/// fell-behind policy.
/// </summary>
/// <remarks>
/// <para>
/// The intended use is: do the work when <see cref="IsDue"/>, then call <see cref="Advance"/>, and
/// wait until <see cref="Next"/> otherwise.
/// </para>
/// <para>
/// Created through the <see cref="Skipping"/> or <see cref="CatchingUp"/> factories.
/// </para>
/// </remarks>
public sealed class FixedRatePacer {
	private readonly long maxCatchUpPeriods; // -1 = skipping

	/// <summary>
	/// The length of one period.
	/// </summary>
	public HostDuration Period { get; private set; }

	/// <summary>
	/// The next deadline.
	/// </summary>
	public HostTick Next { get; private set; }

	private FixedRatePacer(HostDuration period, HostTick first, long maxCatchUpPeriods) {
		validatePeriod(period);
		Period = period;
		Next = first;
		this.maxCatchUpPeriods = maxCatchUpPeriods;
	}

	/// <summary>
	/// Creates a pacer that, when behind, skips the missed deadlines and continues at the first
	/// deadline after the current time, keeping the original phase. Suitable for rendering, where late
	/// frames are worthless.
	/// </summary>
	/// <param name="period">Length of one period; must be positive.</param>
	/// <param name="first">The first deadline.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="period"/> is not positive.
	/// </exception>
	public static FixedRatePacer Skipping(HostDuration period, HostTick first) =>
		new(period, first, -1);

	/// <summary>
	/// Creates a pacer that, when behind, keeps every missed deadline due (so the caller catches
	/// up by doing the work repeatedly) as long as it is at most <paramref name="maxCatchUpPeriods"/>
	/// periods behind, and otherwise gives up and re-anchors one period after the current time.
	/// Suitable for simulation steps, where being late is something to be caught up to.
	/// </summary>
	/// <param name="period">Length of one period; must be positive.</param>
	/// <param name="first">The first deadline.</param>
	/// <param name="maxCatchUpPeriods">How many periods behind is still caught up on.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="period"/> is not positive, or if <paramref name="maxCatchUpPeriods"/>
	/// is negative.
	/// </exception>
	public static FixedRatePacer CatchingUp(HostDuration period, HostTick first, int maxCatchUpPeriods) {
		ArgumentOutOfRangeException.ThrowIfNegative(maxCatchUpPeriods);
		return new FixedRatePacer(period, first, maxCatchUpPeriods);
	}

	/// <summary>
	/// Whether <see cref="Next"/> has been reached at <paramref name="now"/>.
	/// </summary>
	public bool IsDue(HostTick now) => now >= Next;

	/// <summary>
	/// Moves <see cref="Next"/> past the deadline that was just handled, applying this pacer's
	/// policy for falling behind.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the pacer is not due at <paramref name="now"/>, since advancing early would
	/// silently drop a period.
	/// </exception>
	public void Advance(HostTick now) {
		if (!IsDue(now))
			throw new InvalidOperationException($"pacer is not due yet; next deadline = {Next}, now = {now}");
		Next += Period;
		if (Next > now)
			return;
		HostDuration behind = now - Next;
		if (maxCatchUpPeriods < 0)
			Next += Period * checked(behind / Period + 1);
		else if (behind > Period * maxCatchUpPeriods)
			Next = now + Period;
	}

	/// <summary>
	/// Changes the period and restarts the series with <paramref name="next"/> as the next deadline.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="period"/> is not positive.
	/// </exception>
	public void Reset(HostDuration period, HostTick next) {
		validatePeriod(period);
		Period = period;
		Next = next;
	}

	private static void validatePeriod(HostDuration period) {
		if (period <= HostDuration.Zero)
			throw new ArgumentOutOfRangeException(nameof(period), period, "must be positive");
	}
}
