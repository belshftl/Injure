// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using Injure.Time;

namespace Injure.Host;

/// <summary>
/// An instant on the host clock timeline, as produced by an <see cref="IHostClock"/>.
/// </summary>
/// <remarks>
/// <para>
/// The unit is always nanoseconds regardless of clock; the epoch, however, is chosen by the clock.
/// Only comparisons and differences between values from the same clock are meaningful; the raw
/// value of a <see cref="HostTick"/> carries no information on its own. A program is expected to
/// use only one <see cref="IHostClock"/> for all of its <see cref="HostTick"/>s.
/// </para>
/// <para>
/// There is deliberately no conversion from or to raw numbers other than
/// <see cref="DangerousCreateFromRaw(ulong)"/> and <see cref="DangerousGetRaw"/>, since a raw
/// timestamp from some other source (e.g. an OS or library event timestamp) is only a valid
/// <see cref="HostTick"/> if it happens to use the same clock and epoch.
/// </para>
/// <para>
/// All arithmetic operators are checked and throw <see cref="OverflowException"/> on overflow.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is equal to <see cref="Epoch"/>. It is not a
/// "no value" sentinel.
/// </para>
/// </remarks>
public readonly struct HostTick : ITimelineInstant<HostTick, HostDuration> {
	private readonly ulong ns;

	private HostTick(ulong ns) {
		this.ns = ns;
	}

	/// <summary>
	/// The epoch of the clock, i.e. the earliest representable instant.
	/// </summary>
	public static HostTick Epoch => default;

	/// <summary>
	/// Creates a <see cref="HostTick"/> from a raw nanosecond count since the clock's epoch.
	/// </summary>
	/// <remarks>
	/// Meant for <see cref="IHostClock"/> implementations and for converting timestamps that are
	/// known to come from the same clock as the program's <see cref="IHostClock"/>. Using this
	/// with a timestamp from any other clock silently produces a meaningless value.
	/// </remarks>
	public static HostTick DangerousCreateFromRaw(ulong ns) => new(ns);

	/// <summary>
	/// Gets the raw nanosecond count since the clock's epoch.
	/// </summary>
	/// <remarks>
	/// Meant for <see cref="IHostClock"/> implementations and interop with APIs that use the
	/// same clock. See <see cref="DangerousCreateFromRaw(ulong)"/>.
	/// </remarks>
	public ulong DangerousGetRaw() => ns;

	public static HostTick operator +(HostTick left, HostDuration right) => new(addChecked(left.ns, right.Ns));

	public static HostTick operator -(HostTick left, HostDuration right) {
		if (right.Ns == long.MinValue)
			throw new OverflowException();
		return new HostTick(addChecked(left.ns, -right.Ns));
	}

	public static HostDuration operator -(HostTick left, HostTick right) {
		long diff = unchecked((long)(left.ns - right.ns));
		// the wrapped difference is only correct if its sign matches the actual ordering
		if (left.ns >= right.ns != diff >= 0)
			throw new OverflowException();
		return HostDuration.FromNs(diff);
	}

	public static bool operator ==(HostTick left, HostTick right) => left.ns == right.ns;
	public static bool operator !=(HostTick left, HostTick right) => left.ns != right.ns;
	public static bool operator <(HostTick left, HostTick right) => left.ns < right.ns;
	public static bool operator >(HostTick left, HostTick right) => left.ns > right.ns;
	public static bool operator <=(HostTick left, HostTick right) => left.ns <= right.ns;
	public static bool operator >=(HostTick left, HostTick right) => left.ns >= right.ns;

	public bool Equals(HostTick other) => ns == other.ns;
	public override bool Equals(object? obj) => obj is HostTick other && Equals(other);
	public override int GetHashCode() => ns.GetHashCode();
	public int CompareTo(HostTick other) => ns.CompareTo(other.ns);
	public int CompareTo(object? obj) => obj switch {
		null => 1,
		HostTick other => CompareTo(other),
		_ => throw new ArgumentException($"object must be of type {nameof(HostTick)}", nameof(obj)),
	};

	/// <summary>
	/// Formats this instant as <c>T+</c> followed by its raw nanosecond count and <c>ns</c>.
	/// </summary>
	public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

	/// <summary>
	/// Formats this instant as <c>T+</c> followed by its raw nanosecond count, formatted with
	/// <paramref name="format"/>, and <c>ns</c>.
	/// </summary>
	public string ToString(string? format, IFormatProvider? provider) => "T+" + ns.ToString(format, provider) + "ns";

	public bool TryFormat(Span<char> dst, out int written, ReadOnlySpan<char> format, IFormatProvider? provider) {
		written = 0;
		if (dst.Length < 2)
			return false;
		if (!ns.TryFormat(dst[2..], out int n, format, provider) || dst.Length - 2 - n < 2)
			return false;
		dst[0] = 'T';
		dst[1] = '+';
		written = 2 + n;
		dst[written++] = 'n';
		dst[written++] = 's';
		return true;
	}

	private static ulong addChecked(ulong value, long delta) =>
		delta >= 0 ? checked(value + (ulong)delta) : checked(value - (ulong)-delta);
}
