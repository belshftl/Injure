// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Host;

/// <summary>
/// Represents an object that receives intermittent updates of the current <see cref="HostTick"/>
/// value.
/// </summary>
/// <remarks>
/// Updates are typically periodic but are not required to be. The elapsed time between calls may
/// vary and may be zero (in terms of elapsed <see cref="HostTick"/>s, not real time).
/// </remarks>
public interface IHostTickReceiver {
	/// <summary>
	/// Feeds a new sample of the current <see cref="HostTick"/> to this object.
	/// </summary>
	/// <param name="now">
	/// "Right now". Not strictly equivalent to a fresh <see cref="IHostClock.Now"/>; for example,
	/// a caller may snapshot the current tick once and reuse it across multiple operations.
	/// </param>
	/// <remarks>
	/// <para>
	/// Callers must provide values that are monotonically non-decreasing for a given
	/// <see cref="IHostTickReceiver"/> instance; that is, if call B occurs after call A,
	/// B's <paramref name="now"/> must be greater than or equal to A's <paramref name="now"/>.
	/// </para>
	/// <para>
	/// Implementations must not assume or expect the calls to be periodic or otherwise have
	/// predictable timing between each other; however, they may assume that the input
	/// <paramref name="now"/> is monotonically non-decreasing.
	/// </para>
	/// </remarks>
	void Update(HostTick now);
}
