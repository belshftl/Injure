// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Injure.Time;

namespace Injure.Host;

/// <summary>
/// A signed length of time on the host clock timeline, in nanoseconds.
/// </summary>
/// <remarks>
/// <para>
/// The unit is always nanoseconds regardless of clock, same as <see cref="HostTick"/>. The range is
/// roughly ±292 years.
/// </para>
/// <para>
/// All arithmetic operators are checked and throw <see cref="OverflowException"/> on overflow.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is the zero-length duration, same as
/// <see cref="Zero"/>.
/// </para>
/// </remarks>
public readonly struct HostDuration : IRealTimeDuration<HostDuration>, IUnaryNegationOperators<HostDuration, HostDuration>,
	IMultiplyOperators<HostDuration, long, HostDuration>, IDivisionOperators<HostDuration, long, HostDuration>,
	IDivisionOperators<HostDuration, HostDuration, long> {
	private const long nsPerSecond = 1_000_000_000;

	private readonly long ns;

	private HostDuration(long ns) {
		this.ns = ns;
	}

	/// <summary>
	/// The zero-length duration.
	/// </summary>
	public static HostDuration Zero => default;

	/// <summary>
	/// The length of this duration in nanoseconds.
	/// </summary>
	public long Ns => ns;

	/// <summary>
	/// Returns the duration that is <paramref name="ns"/> nanoseconds long.
	/// </summary>
	public static HostDuration FromNs(long ns) => new(ns);

	/// <summary>
	/// Returns the duration that is <paramref name="ms"/> milliseconds long.
	/// </summary>
	/// <exception cref="OverflowException">
	/// Thrown if the result is out of range.
	/// </exception>
	public static HostDuration FromMs(long ms) => new(checked(ms * 1_000_000));

	/// <inheritdoc/>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="sec"/> is NaN or infinite.
	/// </exception>
	/// <exception cref="OverflowException">
	/// Thrown if the result is out of range.
	/// </exception>
	/// <remarks>
	/// Rounds to the nearest nanosecond, away from zero on ties. Inexact for magnitudes above 2^53 ns
	/// (roughly 104 days).
	/// </remarks>
	public static HostDuration FromSeconds(double sec) {
		if (!double.IsFinite(sec))
			throw new ArgumentOutOfRangeException(nameof(sec), sec, "must be finite");
		return new HostDuration(checked((long)Math.Round(sec * nsPerSecond, MidpointRounding.AwayFromZero)));
	}

	/// <summary>
	/// Returns the length of one period at a frequency of <paramref name="hz"/>; for example,
	/// <paramref name="hz"/> = 2.0 yields half a second.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="hz"/> is not positive, or is not finite.
	/// </exception>
	/// <remarks>
	/// Rounds to the nearest nanosecond, and never returns a duration shorter than one nanosecond.
	/// </remarks>
	public static HostDuration PeriodFromHz(double hz) {
		if (!double.IsFinite(hz) || hz <= 0.0)
			throw new ArgumentOutOfRangeException(nameof(hz), hz, "must be positive and finite");
		long ticks = checked((long)Math.Round(nsPerSecond / hz, MidpointRounding.AwayFromZero));
		return new HostDuration(Math.Max(ticks, 1));
	}

	/// <inheritdoc/>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double ToSeconds() => (double)ns / nsPerSecond;

	/// <summary>
	/// Converts this duration to a <see cref="TimeSpan"/>, truncating to its 100ns resolution.
	/// </summary>
	public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(ns / (nsPerSecond / TimeSpan.TicksPerSecond));

	public static HostDuration operator +(HostDuration left, HostDuration right) => new(checked(left.ns + right.ns));
	public static HostDuration operator -(HostDuration left, HostDuration right) => new(checked(left.ns - right.ns));
	public static HostDuration operator -(HostDuration val) => new(checked(-val.ns));
	public static HostDuration operator *(HostDuration left, long right) => new(checked(left.ns * right));

	/// <summary>
	/// Divides a duration by a scalar, truncating toward zero.
	/// </summary>
	public static HostDuration operator /(HostDuration left, long right) => new(checked(left.ns / right));

	/// <summary>
	/// Returns how many whole times <paramref name="right"/> fits into <paramref name="left"/>,
	/// truncating toward zero.
	/// </summary>
	public static long operator /(HostDuration left, HostDuration right) => checked(left.ns / right.ns);

	public static bool operator ==(HostDuration left, HostDuration right) => left.ns == right.ns;
	public static bool operator !=(HostDuration left, HostDuration right) => left.ns != right.ns;
	public static bool operator <(HostDuration left, HostDuration right) => left.ns < right.ns;
	public static bool operator >(HostDuration left, HostDuration right) => left.ns > right.ns;
	public static bool operator <=(HostDuration left, HostDuration right) => left.ns <= right.ns;
	public static bool operator >=(HostDuration left, HostDuration right) => left.ns >= right.ns;

	public bool Equals(HostDuration other) => ns == other.ns;
	public override bool Equals(object? obj) => obj is HostDuration other && Equals(other);
	public override int GetHashCode() => ns.GetHashCode();
	public int CompareTo(HostDuration other) => ns.CompareTo(other.ns);
	public int CompareTo(object? obj) => obj switch {
		null => 1,
		HostDuration other => CompareTo(other),
		_ => throw new ArgumentException($"object must be of type {nameof(HostDuration)}", nameof(obj)),
	};

	/// <summary>
	/// Formats this duration as its nanosecond count followed by <c>ns</c>.
	/// </summary>
	public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

	/// <summary>
	/// Formats this duration as its nanosecond count, formatted with <paramref name="format"/>,
	/// followed by <c>ns</c>.
	/// </summary>
	public string ToString(string? format, IFormatProvider? provider) => ns.ToString(format, provider) + "ns";

	public bool TryFormat(Span<char> dst, out int written, ReadOnlySpan<char> format, IFormatProvider? provider) {
		if (!ns.TryFormat(dst, out written, format, provider) || dst.Length - written < 2) {
			written = 0;
			return false;
		}
		dst[written++] = 'n';
		dst[written++] = 's';
		return true;
	}
}
