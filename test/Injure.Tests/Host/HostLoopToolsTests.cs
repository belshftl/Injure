// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;

namespace Injure.Tests.Host;

public sealed class HostLoopToolsTests {
	private sealed class ManualClock(ulong startNs) : IHostClock {
		public HostTick Now { get; set; } = HostTick.DangerousCreateFromRaw(startNs);
	}

	// simulates a source on a manual clock: WaitUntil either "times out" by advancing the clock to
	// its deadline, or returns early at EventAt
	private sealed class FakeSource(ManualClock clock) : IHostEventSource {
		public HostTick? EventAt;
		public readonly List<string> Log = new();
		public IHostClock Clock => clock;
		public HostDuration WaitGranularity => HostDuration.FromMs(1);
		public bool TryPoll(out HostEvent ev) {
			ev = default;
			return false;
		}
		public bool WaitUntil(HostTick deadline) {
			Log.Add("coarse");
			if (EventAt is HostTick at && at < deadline) {
				clock.Now = at;
				return true;
			}
			clock.Now = deadline;
			return false;
		}
		public void WaitIndefinitely() => throw new NotSupportedException();
		public void Wake() => throw new NotSupportedException();
	}

	private static HostDuration ms(double v) => HostDuration.FromSeconds(v / 1000);

	[Fact]
	public static void WaitGoesCoarseThenPreciseThenSpin() {
		ManualClock clock = new(1_000_000_000);
		FakeSource src = new(clock);
		HostTick deadline = clock.Now + ms(10);
		bool woken = HostWait.Until(
			src,
			deadline,
			d => {
				src.Log.Add("precise");
				clock.Now += d;
			},
			() => {
				src.Log.Add("spin");
				clock.Now += HostDuration.FromNs(30_000);
			}
		);
		Assert.False(woken);
		Assert.True(clock.Now >= deadline);
		Assert.Equal(["coarse", "precise", "spin", "spin", "spin", "spin"], src.Log);
	}

	[Fact]
	public static void WaitReturnsEarlyOnEvent() {
		ManualClock clock = new(1_000_000_000);
		FakeSource src = new(clock) { EventAt = clock.Now + ms(3) };
		HostTick deadline = clock.Now + ms(10);
		Assert.True(HostWait.Until(src, deadline, _ => throw new Exception("should not get here"), () => throw new Exception("nor here")));
		Assert.True(clock.Now < deadline);
	}

	[Fact]
	public static void ShortWaitSkipsCoarseStage() {
		ManualClock clock = new(1_000_000_000);
		FakeSource src = new(clock);
		HostWait.Until(src, clock.Now + ms(1), d => clock.Now += d, () => clock.Now += HostDuration.FromNs(50_000));
		Assert.DoesNotContain("coarse", src.Log);
	}

	[Fact]
	public static void WaitWithPastDeadlineReturnsImmediately() {
		ManualClock clock = new(1_000_000_000);
		FakeSource src = new(clock);
		Assert.False(HostWait.Until(src, clock.Now - ms(1), _ => throw new Exception(), () => throw new Exception()));
		Assert.Empty(src.Log);
	}

	[Fact]
	public static void SkippingPacerKeepsPhase() {
		var t0 = HostTick.DangerousCreateFromRaw(1_000_000_000);
		var p = FixedRatePacer.Skipping(ms(10), t0);
		Assert.False(p.IsDue(t0 - ms(1)));
		p.Advance(t0);
		Assert.Equal(t0 + ms(10), p.Next);
		// 35ms late: skip to the first boundary after now, i.e. t0 + 50ms
		p.Advance(t0 + ms(45));
		Assert.Equal(t0 + ms(50), p.Next);
		Assert.Throws<InvalidOperationException>(() => p.Advance(t0 + ms(49)));
	}

	[Fact]
	public static void CatchingUpPacerCatchesUpThenReanchors() {
		var t0 = HostTick.DangerousCreateFromRaw(1_000_000_000);
		var p = FixedRatePacer.CatchingUp(ms(10), t0, maxCatchUpPeriods: 4);
		HostTick now = t0 + ms(25);
		int runs = 0;
		while (p.IsDue(now)) {
			p.Advance(now);
			runs++;
		}
		Assert.Equal(3, runs); // t0, t0+10, t0+20
		Assert.Equal(t0 + ms(30), p.Next);

		now = t0 + ms(200); // way behind: give up and re-anchor
		p.Advance(now);
		Assert.Equal(now + ms(10), p.Next);
	}

	[Fact]
	public static void PacerRejectsBadPeriods() {
		Assert.Throws<ArgumentOutOfRangeException>(() => FixedRatePacer.Skipping(HostDuration.Zero, default));
		Assert.Throws<ArgumentOutOfRangeException>(() => FixedRatePacer.CatchingUp(ms(1), default, -1));
	}

	[Fact]
	public static void CompositeMergesByTickAndPrefersPrimaryOnTies() {
		StopwatchHostClock clock = StopwatchHostClock.Instance;
		using QueuedHostEventSource primary = new(clock);
		using QueuedHostEventSource secondary = new(clock);
		using CompositeHostEventSource c = new(primary, secondary);
		HostTick t = clock.Now;
		primary.Post(HostEvent.Quit(t + ms(2)));
		primary.Post(HostEvent.Quit(t + ms(5)));
		secondary.Post(HostEvent.Quit(t + ms(1)));
		secondary.Post(HostEvent.Quit(t + ms(5)));
		List<HostTick> order = new();
		while (c.TryPoll(out HostEvent ev))
			order.Add(ev.Tick);
		Assert.Equal([t + ms(1), t + ms(2), t + ms(5), t + ms(5)], order);
	}

	[Fact]
	public static void SecondaryWakesCompositeWaitFromAnotherThread() {
		StopwatchHostClock clock = StopwatchHostClock.Instance;
		using QueuedHostEventSource primary = new(clock);
		using QueuedHostEventSource secondary = new(clock);
		using CompositeHostEventSource c = new(primary, secondary);
		Task.Run(async () => {
			await Task.Delay(50);
			secondary.Post(HostEvent.Quit(clock.Now));
		}, TestContext.Current.CancellationToken);
		HostTick start = clock.Now;
		Assert.True(c.WaitUntil(start + ms(5000)));
		Assert.True(clock.Now - start < ms(2000));
		Assert.True(c.TryPoll(out HostEvent ev));
		Assert.Equal(HostEventKind.Quit, ev.Kind);
	}

	[Fact]
	public static void CompositeValidatesAndUnregisters() {
		using QueuedHostEventSource primary = new(StopwatchHostClock.Instance);
		using QueuedHostEventSource other = new(new ManualClock(0));
		Assert.Throws<ArgumentException>(() => new CompositeHostEventSource(primary, other));

		using QueuedHostEventSource secondary = new(StopwatchHostClock.Instance);
		CompositeHostEventSource c = new(primary, secondary);
		Assert.Throws<InvalidOperationException>(() => new CompositeHostEventSource(primary, secondary)); // callback taken
		c.Dispose();
		using CompositeHostEventSource again = new(primary, secondary); // freed by Dispose
		Assert.Throws<ObjectDisposedException>(() => c.TryPoll(out _));
	}
}
