// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;
using Injure.Sched.Tickers;

namespace Injure.Tests.Sched;

public static class TickerSchedulerTests {
	private sealed class ManualClock(ulong startNs) : IHostClock {
		public HostTick Now { get; private set; } = HostTick.DangerousCreateFromRaw(startNs);
		public void Advance(HostDuration d) => Now += d;
	}

	private static readonly HostDuration ms10 = HostDuration.FromMs(10);

	private static (TickerScheduler, List<TickCallbackTimingInfo>) setup(ManualClock clock, TickerOptions options) {
		TickerScheduler sched = new(clock, new TickerSchedulerOptions());
		List<TickCallbackTimingInfo> calls = new();
		TickerHandle handle = sched.Add(new TickerSpec(new TickerTiming(ms10), options));
		handle.Subscribe((in info, in _) => calls.Add(info));
		sched.ApplyPending();
		return (sched, calls);
	}

	[Fact]
	public static void FiresOncePerPeriod() {
		ManualClock clock = new(1_000_000_000);
		(TickerScheduler sched, List<TickCallbackTimingInfo> calls) = setup(clock, TickerOptions.Default);

		sched.RunDueTickers(); // commit time + zero offset is due immediately
		Assert.Single(calls);
		sched.RunDueTickers();
		Assert.Single(calls);

		clock.Advance(ms10);
		sched.RunDueTickers();
		Assert.Equal(2, calls.Count);
		Assert.Equal(ms10, calls[1].Elapsed);
		Assert.Equal(HostDuration.Zero, calls[1].Late);
	}

	[Fact]
	public static void OnceModeSkipsMissedPeriods() {
		ManualClock clock = new(1_000_000_000);
		(TickerScheduler sched, List<TickCallbackTimingInfo> calls) = setup(clock, TickerOptions.Default);
		sched.RunDueTickers();

		clock.Advance(ms10 * 3 + HostDuration.FromMs(5));
		sched.RunDueTickers();
		Assert.Equal(2, calls.Count);
		Assert.Equal(HostDuration.FromMs(5) + ms10 * 2, calls[1].Late);
		Assert.True(sched.TryGetEarliestNextAt(out HostTick next));
		Assert.Equal(calls[1].ScheduledAt + ms10 * 3, next);
	}

	[Fact]
	public static void BatchCallLimitSplitsCatchUpAcrossRuns() {
		ManualClock clock = new(1_000_000_000);
		TickerScheduler sched = new(clock, new TickerSchedulerOptions(BatchCallLimit: 3, EventPollInterval: 0));
		List<TickCallbackTimingInfo> calls = new();
		TickerOptions catchUp = TickerOptions.Default with { OverrunMode = TickerOverrunMode.CatchUp, MaxBurst = 100 };
		sched.Add(new TickerSpec(new TickerTiming(ms10), catchUp)).Subscribe((in info, in _) => calls.Add(info));
		sched.ApplyPending();
		sched.RunDueTickers();
		Assert.Single(calls);

		// 5 periods behind: 3 calls now, the other 2 in the next run
		clock.Advance(ms10 * 5);
		sched.RunDueTickers();
		Assert.Equal(4, calls.Count);
		sched.RunDueTickers();
		Assert.Equal(6, calls.Count);
		sched.RunDueTickers();
		Assert.Equal(6, calls.Count);
	}

	[Fact]
	public static void EventPollIntervalSplitsCatchUpAcrossRuns() {
		ManualClock clock = new(1_000_000_000);
		TickerScheduler sched = new(clock, new TickerSchedulerOptions(BatchCallLimit: 64, EventPollInterval: 2));
		List<TickCallbackTimingInfo> calls = new();
		TickerOptions catchUp = TickerOptions.Default with { OverrunMode = TickerOverrunMode.CatchUp, MaxBurst = 100 };
		sched.Add(new TickerSpec(new TickerTiming(ms10), catchUp)).Subscribe((in info, in _) => calls.Add(info));
		sched.ApplyPending();
		sched.RunDueTickers();
		Assert.Single(calls);

		// 4 periods behind: 2 calls per run, like BatchCallLimit
		clock.Advance(ms10 * 4);
		sched.RunDueTickers();
		Assert.Equal(3, calls.Count);
		sched.RunDueTickers();
		Assert.Equal(5, calls.Count);
	}

	[Fact]
	public static void FirstCallbackAtClockEpochDoesNotUnderflow() {
		// "previous" times of the first callback would be one period before the epoch
		ManualClock clock = new(0);
		(TickerScheduler sched, List<TickCallbackTimingInfo> calls) = setup(clock, TickerOptions.Default);
		sched.RunDueTickers();
		TickCallbackTimingInfo call = Assert.Single(calls);
		Assert.Equal(HostTick.Epoch, call.PreviousScheduledAt);
		Assert.Equal(HostTick.Epoch, call.PreviousActualAt);
	}

	[Fact]
	public static void RejectsNonPositivePeriod() {
		TickerScheduler sched = new(new ManualClock(0), new TickerSchedulerOptions());
		Assert.Throws<ArgumentOutOfRangeException>(() => sched.Add(new TickerSpec(new TickerTiming(HostDuration.Zero), TickerOptions.Default)));
		Assert.Throws<ArgumentOutOfRangeException>(() => sched.Add(new TickerSpec(new TickerTiming(-ms10), TickerOptions.Default)));
	}

	[Fact]
	public static void DefaultOptionsAreRejected() =>
		Assert.Equal(
			"options",
			Assert.Throws<ArgumentInvalidDefaultException>(static () => new TickerScheduler(new ManualClock(0), default)).ParamName
		);
}
