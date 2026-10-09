// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Host;

/// <summary>
/// A source of <see cref="HostEvent"/>s, plus the means to block until one arrives.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TryPoll(out HostEvent)"/>, <see cref="WaitUntil(HostTick)"/>, and
/// <see cref="WaitIndefinitely()"/> are to be
/// called from one thread at a time; implementations may restrict them further to one specific
/// thread. <see cref="Wake()"/> must be callable from any thread.
/// </para>
/// <para>
/// Events are returned in the order the source received them. Their <see cref="HostEvent.Tick"/>s
/// are on <see cref="Clock"/>, but are only guaranteed to be non-decreasing if the implementation
/// says so.
/// </para>
/// </remarks>
public interface IHostEventSource {
	/// <summary>
	/// The clock that this source's event timestamps and wait deadlines are on.
	/// </summary>
	IHostClock Clock { get; }

	/// <summary>
	/// The coarsest granularity at which <see cref="WaitUntil(HostTick)"/> can hit its deadline.
	/// </summary>
	/// <remarks>
	/// <see cref="WaitUntil(HostTick)"/> may return up to this much before its deadline; a caller that
	/// needs better precision has to wait out the remainder itself.
	/// </remarks>
	HostDuration WaitGranularity { get; }

	/// <summary>
	/// Removes and returns the next pending event, if there is one. Never blocks.
	/// </summary>
	bool TryPoll(out HostEvent ev);

	/// <summary>
	/// Blocks until an event is pending, <see cref="Wake()"/> is called, or
	/// <paramref name="deadline"/> is (nearly) reached.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if woken by an event or <see cref="Wake()"/>; <see langword="false"/>
	/// if the deadline was reached, or is less than <see cref="WaitGranularity"/> away.
	/// </returns>
	/// <remarks>
	/// A return value of <see langword="true"/> does not guarantee that
	/// <see cref="TryPoll(out HostEvent)"/> will
	/// return an event, since a <see cref="Wake()"/> call produces none.
	/// </remarks>
	bool WaitUntil(HostTick deadline);

	/// <summary>
	/// Blocks until an event is pending or <see cref="Wake()"/> is called.
	/// </summary>
	/// <remarks>
	/// Returning does not guarantee that <see cref="TryPoll(out HostEvent)"/> returns an event, since
	/// a <see cref="Wake()"/> call produces none.
	/// </remarks>
	void WaitIndefinitely();

	/// <summary>
	/// Makes a current or the next <see cref="WaitUntil(HostTick)"/>/<see cref="WaitIndefinitely()"/>
	/// call return early. Callable from any thread.
	/// </summary>
	/// <remarks>
	/// Calls made before the waiting thread gets to run again may coalesce into one wakeup.
	/// </remarks>
	void Wake();
}
