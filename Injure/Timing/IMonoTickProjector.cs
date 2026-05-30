// SPDX-License-Identifier: MIT

namespace Injure.Timing;

/// <summary>
/// Represents a projection from <see cref="MonoTick"/> values to a domain-defined time position.
/// </summary>
public interface IPartialMonoTickProjector<T> where T : struct, ITimelineScalar<T> {
	/// <summary>
	/// Attempts to project a <see cref="MonoTick"/> value into the target domain.
	/// </summary>
	/// <param name="tick">Tick value to project.</param>
	/// <param name="value">On success, the projected value.</param>
	/// <returns>
	/// <see langword="true"/> if the input value has a meaningful projection and as such the
	/// call succeeded; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Unless the implementation guarantees otherwise, the projection may not be exact and
	/// may be interpolated, extrapolated, smoothed, delayed, quantized, or otherwise approximate.
	/// </para>
	/// <para>
	/// This method should not consume samples or perform recalibration, and implementors should treat
	/// this as a query of their current state. Benign internal caching is allowed. Stateful recalibration
	/// or updates should use other APIs, such as implementing <see cref="IMonoTickReceiver"/>.
	/// </para>
	/// </remarks>
	bool TryGetAt(MonoTick tick, out T value);
}

/// <summary>
/// Represents a total projection from <see cref="MonoTick"/> to a domain-defined time position.
/// </summary>
public interface IMonoTickProjector<T> : IPartialMonoTickProjector<T> where T : struct, ITimelineScalar<T> {
	bool IPartialMonoTickProjector<T>.TryGetAt(MonoTick tick, out T value) {
		value = GetAt(tick);
		return true;
	}

	/// <summary>
	/// Projects a <see cref="MonoTick"/> value into the target domain.
	/// </summary>
	/// <param name="tick">Tick value to project.</param>
	/// <returns>
	/// The projected value.
	/// </returns>
	/// <remarks>
	/// <para>
	/// The returned value is guaranteed to be valid within the domain but is not necessarily
	/// within some higher-level bounds; for example, a projection to timestamp values within
	/// an audio track may yield timestamps that are past the end of the track.
	/// </para>
	/// <para>
	/// Unless the implementation guarantees otherwise, the projection may not be exact and
	/// may be interpolated, extrapolated, smoothed, delayed, quantized, or otherwise approximate.
	/// </para>
	/// <para>
	/// This method should not consume samples or perform recalibration, and implementors should treat
	/// this as a query of their current state. Benign internal caching is allowed. Stateful recalibration
	/// or updates should use other APIs, such as implementing <see cref="IMonoTickReceiver"/>.
	/// </para>
	/// </remarks>
	T GetAt(MonoTick tick);
}
