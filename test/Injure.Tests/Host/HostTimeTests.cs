// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Host;

namespace Injure.Tests.Host;

public sealed class HostTimeTests {
	private static HostTick tick(ulong ns) => HostTick.DangerousCreateFromRaw(ns);
	private static HostDuration dur(long ns) => HostDuration.FromNs(ns);

	[Fact]
	public static void InstantDurationRoundtrips() {
		HostTick a = tick(1_000);
		HostTick b = tick(250);
		Assert.Equal(dur(750), a - b);
		Assert.Equal(dur(-750), b - a);
		Assert.Equal(a, b + (a - b));
		Assert.Equal(b, a + (b - a));
		Assert.Equal(b, a - dur(750));
	}

	[Fact]
	public static void InstantArithmeticIsChecked() {
		Assert.Throws<OverflowException>(() => tick(5) - dur(6));
		Assert.Throws<OverflowException>(() => tick(5) + dur(-6));
		Assert.Throws<OverflowException>(() => tick(ulong.MaxValue) + dur(1));
		Assert.Throws<OverflowException>(() => tick(0) - dur(long.MinValue));
		// difference doesn't fit in a long
		Assert.Throws<OverflowException>(() => tick(ulong.MaxValue) - tick(0));
		Assert.Throws<OverflowException>(() => tick(0) - tick(ulong.MaxValue));
		Assert.Equal(dur(long.MaxValue), tick(long.MaxValue) - tick(0));
		Assert.Equal(dur(-long.MaxValue), tick(0) - tick(long.MaxValue));
	}

	[Fact]
	public static void DurationArithmeticIsChecked() {
		Assert.Throws<OverflowException>(() => dur(long.MaxValue) + dur(1));
		Assert.Throws<OverflowException>(() => -dur(long.MinValue));
		Assert.Throws<OverflowException>(() => dur(long.MaxValue) * 2);
		Assert.Equal(dur(3), dur(10) / 3);
		Assert.Equal(3L, dur(10) / dur(3));
	}

	[Fact]
	public static void DurationConversionsWork() {
		Assert.Equal(dur(1_500_000), HostDuration.FromSeconds(0.0015));
		Assert.Equal(dur(500_000_000), HostDuration.PeriodFromHz(2.0));
		Assert.Equal(dur(1), HostDuration.PeriodFromHz(1e12));
		Assert.Equal(0.25, dur(250_000_000).ToSeconds());
		Assert.Equal(TimeSpan.FromMilliseconds(1), HostDuration.FromMs(1).ToTimeSpan());
		Assert.Throws<ArgumentOutOfRangeException>(() => HostDuration.FromSeconds(double.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(() => HostDuration.PeriodFromHz(0.0));
		Assert.Throws<OverflowException>(() => HostDuration.FromSeconds(1e12));
	}

	[Fact]
	public static void FormattingWorks() {
		Assert.Equal("T+42ns", tick(42).ToString());
		Assert.Equal("-7ns", dur(-7).ToString());
		Span<char> buf = stackalloc char[16];
		Assert.True(tick(42).TryFormat(buf, out int n, default, null));
		Assert.Equal("T+42ns", buf[..n].ToString());
		Assert.False(tick(42).TryFormat(buf[..5], out n, default, null));
		Assert.Equal(0, n);
	}

	[Fact]
	public static void StopwatchClockIsMonotonic() {
		HostTick prev = StopwatchHostClock.Instance.Now;
		for (int i = 0; i < 10_000; i++) {
			HostTick now = StopwatchHostClock.Instance.Now;
			Assert.True(now >= prev);
			prev = now;
		}
	}

	[Fact]
	public static void WindowIdsAreUniqueAndDefaultIsInvalid() {
		Assert.False(default(HostWindowId).IsValid);
		var a = HostWindowId.Allocate();
		var b = HostWindowId.Allocate();
		Assert.True(a.IsValid);
		Assert.NotEqual(a, b);
	}
}
