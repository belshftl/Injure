// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Host;

/// <summary>
/// An <see cref="IHostEventSource"/> that has a "new events available" callback. This allows it to
/// inform a waiter blocked elsewhere that it has new events, which is what lets it be a secondary
/// source of a <see cref="CompositeHostEventSource"/>.
/// </summary>
/// <remarks>
/// Sources that only learn about events while being polled on their own thread (such as SDL's event
/// queue) can't meaningfully implement this.
/// </remarks>
public interface INotifyingHostEventSource : IHostEventSource {
	/// <summary>
	/// Sets the callback to invoke whenever an event becomes available to
	/// <see cref="IHostEventSource.TryPoll"/>, or clears it with <see langword="null"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if an attempt is made to set a callback while another is set; it must be cleared first.
	/// </exception>
	/// <remarks>
	/// The callback may be invoked from any thread, may be invoked more than once per event, and must
	/// not block. Implementations must invoke it after the event has become pollable.
	/// </remarks>
	void SetEventsAvailableCallback(Action? callback);
}
