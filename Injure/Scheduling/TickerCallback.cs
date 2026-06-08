// SPDX-License-Identifier: MIT

using Injure.Timing;

namespace Injure.Scheduling;

public readonly record struct TickCallbackTimingInfo(
	MonoTick ScheduledAt,
	MonoTick ActualAt,
	MonoTick PreviousScheduledAt,
	MonoTick PreviousActualAt,
	MonoTick Period,
	MonoTick Elapsed,
	MonoTick Late
);

public readonly struct TickDeadline {
	private readonly MonoTick deadlineAt;
	internal TickDeadline(MonoTick deadlineAt) {
		this.deadlineAt = deadlineAt;
	}

	public bool HasDeadline => deadlineAt != MonoTick.Zero;
	public MonoTick DeadlineAt => deadlineAt;

	public MonoTick Remaining {
		get {
			if (!HasDeadline)
				return MonoTick.Zero;
			var now = MonoTick.GetCurrent();
			return now < deadlineAt ? deadlineAt - now : MonoTick.Zero;
		}
	}

	public bool IsOverrun {
		get {
			if (!HasDeadline)
				return false;
			return MonoTick.GetCurrent() >= deadlineAt;
		}
	}
}

public delegate void TickerCallback(in TickCallbackTimingInfo info, in TickDeadline deadline);
