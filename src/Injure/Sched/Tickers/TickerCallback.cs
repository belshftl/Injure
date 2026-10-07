// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;

namespace Injure.Sched.Tickers;

public readonly record struct TickCallbackTimingInfo(
	HostTick ScheduledAt,
	HostTick ActualAt,
	HostTick PreviousScheduledAt,
	HostTick PreviousActualAt,
	HostDuration Period,
	HostDuration Elapsed,
	HostDuration Late
);

public readonly struct TickDeadline {
	private readonly IHostClock? clock;
	private readonly HostTick deadlineAt;

	internal TickDeadline(IHostClock clock, HostTick deadlineAt) {
		this.clock = clock;
		this.deadlineAt = deadlineAt;
	}

	public bool HasDeadline => clock is not null;
	public HostTick DeadlineAt => deadlineAt;

	public HostDuration Remaining {
		get {
			if (clock is null)
				return HostDuration.Zero;
			HostTick now = clock.Now;
			return now < deadlineAt ? deadlineAt - now : HostDuration.Zero;
		}
	}

	public bool IsOverrun => clock is not null && clock.Now >= deadlineAt;
}

public delegate void TickerCallback(in TickCallbackTimingInfo info, in TickDeadline deadline);
