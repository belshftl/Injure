// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Time;

namespace Injure.Host;

/// <summary>
/// The source of <see cref="HostTick"/> values for a program.
/// </summary>
/// <remarks>
/// <para>
/// A program is expected to use a single clock for everything that produces or consumes
/// <see cref="HostTick"/>s, including event timestamps; values from two different clocks yield
/// meaningless results when compared.
/// </para>
/// <para>
/// Implementations must be monotonically non-decreasing across all threads, must count elapsed
/// real time (as opposed to e.g. CPU time), and must make <see cref="Now"/> thread-safe.
/// </para>
/// </remarks>
public interface IHostClock : IMonoCurrentSampleable<HostTick> {
	HostTick ICurrentSampleable<HostTick>.SampleCurrent() => Now;

	/// <summary>
	/// The current instant.
	/// </summary>
	/// <remarks>
	/// Callable from any thread, and safe for concurrent calls from multiple threads.
	/// </remarks>
	HostTick Now { get; }
}
