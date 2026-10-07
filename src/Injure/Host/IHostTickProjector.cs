// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Time;

namespace Injure.Host;

/// <summary>
/// Represents a projection from <see cref="HostTick"/> values to a domain-defined time position.
/// </summary>
public interface IPartialHostTickProjector<T> where T : struct, ITimelineInstant<T> {
	/// <summary>
	/// Attempts to project a <see cref="HostTick"/> value into the target domain.
	/// </summary>
	/// <param name="tick">Tick value to project.</param>
	/// <param name="value">On success, the projected value.</param>
	/// <returns>
	/// <see langword="true"/> if the input value has a meaningful projection and as such the call
	/// succeeded; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Unless the implementation guarantees otherwise, the projection may not be exact and may be
	/// interpolated, extrapolated, smoothed, delayed, quantized, or otherwise approximate.
	/// </para>
	/// <para>
	/// This method should not consume samples or perform recalibration, and implementors should treat
	/// this as a query of their current state. Benign internal caching is allowed. Stateful
	/// recalibration or updates should use other APIs, such as implementing
	/// <see cref="IHostTickReceiver"/>.
	/// </para>
	/// </remarks>
	bool TryGetAt(HostTick tick, out T value);
}

/// <summary>
/// Represents a total projection from <see cref="HostTick"/> to a domain-defined time position.
/// </summary>
public interface IHostTickProjector<T> : IPartialHostTickProjector<T> where T : struct, ITimelineInstant<T> {
	bool IPartialHostTickProjector<T>.TryGetAt(HostTick tick, out T value) {
		value = GetAt(tick);
		return true;
	}

	/// <summary>
	/// Projects a <see cref="HostTick"/> value into the target domain.
	/// </summary>
	/// <param name="tick">Tick value to project.</param>
	/// <returns>
	/// The projected value.
	/// </returns>
	/// <remarks>
	/// <para>
	/// The returned value is guaranteed to be valid within the domain but is not necessarily within
	/// some higher-level bounds; for example, a projection to timestamp values within an audio track
	/// may yield timestamps that are past the end of the track.
	/// </para>
	/// <para>
	/// Unless the implementation guarantees otherwise, the projection may not be exact and may be
	/// interpolated, extrapolated, smoothed, delayed, quantized, or otherwise approximate.
	/// </para>
	/// <para>
	/// This method should not consume samples or perform recalibration, and implementors should treat
	/// this as a query of their current state. Benign internal caching is allowed. Stateful
	/// recalibration or updates should use other APIs, such as implementing
	/// <see cref="IHostTickReceiver"/>.
	/// </para>
	/// </remarks>
	T GetAt(HostTick tick);
}
