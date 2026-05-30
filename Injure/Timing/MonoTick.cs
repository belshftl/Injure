// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;

using Injure.Core;
using Injure.Internals.Analyzers.Attributes;

namespace Injure.Timing;

/// <summary>
/// Represents a process-wide, monotonically incrementing tick value.
/// </summary>
[StronglyTypedInt(typeof(ulong))]
public readonly partial struct MonoTick : IRealTimeScalar<MonoTick> {
	/// <summary>
	/// How many ticks make up a second.
	/// </summary>
	public static readonly MonoTick Frequency = (MonoTick)1000000000;

	/// <summary>
	/// The current tick value.
	/// </summary>
	public static MonoTick GetCurrent() => SDLOwner.MonoTickGetCurrent();

	/// <summary>
	/// Converts this <see cref="MonoTick"/> value to seconds.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double ToSeconds() => (double)Value / Frequency.Value;

	/// <summary>
	/// Converts this <see cref="MonoTick"/> value to nanoseconds.
	/// </summary>
	/// <remarks>
	/// Mostly a convenience for <see cref="ToSeconds()"/> + a division, but the implementation
	/// may also be more optimized than converting to seconds + dividing.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ulong ToNanoseconds() => Value;

	/// <summary>
	/// Converts an amount of seconds to a <see cref="MonoTick"/> value.
	/// </summary>
	/// <remarks>
	/// May be inaccurate for <paramref name="sec"/> values higher than 2^53 nanoseconds, or
	/// roughly 9007199.255 seconds, or around 104 days.
	/// </remarks>
	public static MonoTick FromSeconds(double sec) {
		ArgumentOutOfRangeException.ThrowIfNegative(sec);
		ulong ticks = checked((ulong)Math.Round(sec * Frequency.Value, MidpointRounding.AwayFromZero));
		return (MonoTick)ticks;
	}

	/// <summary>
	/// Converts a frequency in Hz to the length in time of a single period. For example,
	/// <paramref name="hz"/> = 2.0 yields a value equal to 1/2 of a second.
	/// </summary>
	public static MonoTick PeriodFromHz(double hz) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hz);
		ulong ticks = checked((ulong)Math.Round(Frequency.Value / hz, MidpointRounding.AwayFromZero));
		return (MonoTick)Math.Max(ticks, 1);
	}
}
