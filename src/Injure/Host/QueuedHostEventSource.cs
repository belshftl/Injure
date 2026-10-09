// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace Injure.Host;

/// <summary>
/// An <see cref="IHostEventSource"/> backed by a thread-safe queue that events are
/// <see cref="Post(in HostEvent)"/>ed to, e.g. from a UI toolkit's thread.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Post(in HostEvent)"/> and <see cref="Wake()"/> are callable from any thread; the
/// other members follow the usual <see cref="IHostEventSource"/> rule of one consuming thread at a
/// time.
/// </para>
/// <para>
/// Events are returned in posting order. Their ticks have to be on <see cref="Clock"/>; this
/// invariant is not checked.
/// </para>
/// </remarks>
public sealed class QueuedHostEventSource : INotifyingHostEventSource, IDisposable {
	private readonly ConcurrentQueue<HostEvent> queue = new();
	private readonly ManualResetEventSlim signal = new(false);
	private Action? eventsAvailable;
	private int disposed;

	/// <inheritdoc/>
	public IHostClock Clock { get; }

	// inheritdoc caused the inherited <remarks> to override the new ones entirely

	/// <summary>
	/// The coarsest granularity at which <see cref="WaitUntil(HostTick)"/> can hit its deadline. See
	/// <see cref="IHostEventSource.WaitGranularity"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A conservative estimate of OS wait timer resolution: 16ms on Windows (a little bit over the
	/// default timer resolution there), 1ms elsewhere.
	/// </para>
	/// <para>
	/// Dev note: this could use more testing in the future before stable; it's fine for now.
	/// </para>
	/// </remarks>
	public HostDuration WaitGranularity { get; } = HostDuration.FromMs(OperatingSystem.IsWindows() ? 16 : 1);

	/// <summary>
	/// Creates an empty source whose events are on <paramref name="clock"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="clock"/> is <see langword="null"/>.
	/// </exception>
	public QueuedHostEventSource(IHostClock clock) {
		ArgumentNullException.ThrowIfNull(clock);
		Clock = clock;
	}

	/// <summary>
	/// Adds an event to the queue. Callable from any thread.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="ev"/> is <see langword="default"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this source has been disposed.
	/// </exception>
	public void Post(in HostEvent ev) {
		if (ev.Kind == default)
			throw new ArgumentException("cannot post a default HostEvent", nameof(ev));
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		queue.Enqueue(ev);
		signal.Set();
		Volatile.Read(ref eventsAvailable)?.Invoke();
	}

	/// <inheritdoc/>
	public bool TryPoll(out HostEvent ev) => queue.TryDequeue(out ev);

	/// <inheritdoc/>
	public bool WaitUntil(HostTick deadline) {
		for (;;) {
			if (!queue.IsEmpty)
				return true;
			HostDuration remaining = deadline - Clock.Now;
			if (remaining < WaitGranularity)
				return false;
			if (signal.Wait(remaining.ToTimeSpan())) {
				signal.Reset();
				return true;
			}
		}
	}

	/// <inheritdoc/>
	public void WaitIndefinitely() {
		if (!queue.IsEmpty)
			return;
		signal.Wait();
		signal.Reset();
	}

	/// <inheritdoc/>
	public void Wake() => signal.Set();

	/// <inheritdoc/>
	public void SetEventsAvailableCallback(Action? callback) {
		if (callback is null) {
			Volatile.Write(ref eventsAvailable, null);
			return;
		}
		if (Interlocked.CompareExchange(ref eventsAvailable, callback, null) is not null)
			throw new InvalidOperationException("an events-available callback is already set");
		if (!queue.IsEmpty)
			callback(); // events posted before registration would otherwise go unnoticed
	}

	/// <summary>
	/// Releases the wait handle.
	/// </summary>
	public void Dispose() {
		Volatile.Write(ref disposed, 1);
		signal.Dispose();
	}
}
