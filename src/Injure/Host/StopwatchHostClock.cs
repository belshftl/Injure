// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;

namespace Injure.Host;

/// <summary>
/// An <see cref="IHostClock"/> backed by <see cref="Stopwatch.GetTimestamp()"/>.
/// </summary>
/// <remarks>
/// <para>
/// The epoch is the epoch of the OS monotonic clock that <see cref="Stopwatch"/> uses, and
/// stays the same for the lifetime of the process.
/// </para>
/// <para>
/// Intended for programs that are implementing a custom <see cref="IHostEventSource"/>/etc. and
/// need a clock; the common SDL case should use <see cref="Sdl.SdlHostClock"/> instead.
/// </para>
/// </remarks>
public sealed class StopwatchHostClock : IHostClock {
	private const long nsPerSecond = 1_000_000_000;

	/// <summary>
	/// The shared instance.
	/// </summary>
	public static StopwatchHostClock Instance { get; } = new();

	private StopwatchHostClock() {
	}

	/// <inheritdoc/>
	public HostTick Now {
		get {
			long ts = Stopwatch.GetTimestamp();
			ulong ns = Stopwatch.Frequency == nsPerSecond
				? (ulong)ts
				: (ulong)((UInt128)(ulong)ts * nsPerSecond / (ulong)Stopwatch.Frequency);
			return HostTick.DangerousCreateFromRaw(ns);
		}
	}
}
