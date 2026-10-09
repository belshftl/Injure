// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Time;

namespace Injure.Host;

/// <summary>
/// Precise waiting on an <see cref="IHostEventSource"/>'s clock.
/// </summary>
public static class HostWait {
	// how far before the deadline the coarse (interruptible) wait aims, on top of the source's
	// granularity, to absorb OS wakeup latency
	internal static readonly HostDuration CoarseMargin = HostDuration.FromSeconds(0.0005);
	// below this much remaining time, spin instead of sleeping
	internal static readonly HostDuration SpinThreshold = HostDuration.FromSeconds(0.0001);

	/// <summary>
	/// Blocks until <paramref name="deadline"/> on <paramref name="source"/>'s clock, or until
	/// <paramref name="source"/> has an event or is woken.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if the method returned early because of an event or
	/// <see cref="IHostEventSource.Wake()"/>; otherwise, i.e. if the deadline was reached,
	/// <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="source"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Waits in three stages: an interruptible wait on the source
	/// (<see cref="IHostEventSource.WaitUntil(HostTick)"/>) until shortly before the deadline, then a
	/// <see cref="PreciseWait"/> sleep, then a short spin. Returns no earlier than the deadline
	/// unless woken, and typically within a few microseconds after it.
	/// </para>
	/// <para>
	/// Only the first stage is capable of noticing events and wakeups. As such, if one arrives during
	/// the last <see cref="IHostEventSource.WaitGranularity"/> plus about 0.5ms, it is only handled
	/// once the deadline is reached.
	/// </para>
	/// </remarks>
	public static bool Until(IHostEventSource source, HostTick deadline) {
		ArgumentNullException.ThrowIfNull(source);
		return Until(source, deadline, static d => PreciseWait.WaitPreferUndershoot(d.Ns), static () => Thread.SpinWait(128));
	}

	internal static bool Until(IHostEventSource source, HostTick deadline, Action<HostDuration> preciseWait, Action spin) {
		IHostClock clock = source.Clock;
		HostDuration coarseCutoff = source.WaitGranularity + CoarseMargin;
		for (;;) {
			HostTick now = clock.Now;
			if (now >= deadline)
				return false;
			HostDuration remaining = deadline - now;
			if (remaining > coarseCutoff) {
				if (source.WaitUntil(deadline - CoarseMargin))
					return true;
			} else if (remaining > SpinThreshold) {
				preciseWait(remaining - SpinThreshold);
			} else {
				spin();
			}
		}
	}
}
