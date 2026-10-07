// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Host;

/// <summary>
/// Combines several event sources into one: waits block on a primary source, and secondary
/// sources wake if any of them have events.
/// </summary>
/// <remarks>
/// <para>
/// All sources must share the same <see cref="IHostClock"/> instance. The primary can be any
/// source (typically the one tied to the OS event loop, such as SDL's); secondaries have to be
/// <see cref="INotifyingHostEventSource"/>s. The composite registers itself as each secondary's
/// events-available callback for as long as it is not disposed.
/// </para>
/// </remarks>
public sealed class CompositeHostEventSource : IHostEventSource, IDisposable {
	private readonly IHostEventSource[] sources; // primary first
	private readonly HostEvent[] pending;
	private readonly bool[] hasPending;
	private readonly Action wakePrimary;
	private bool disposed;

	/// <summary>
	/// Creates a composite of <paramref name="primary"/> and <paramref name="secondaries"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="primary"/> or any element of <paramref name="secondaries"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if a source appears more than once, or if the sources don't share the same
	/// <see cref="IHostClock"/> instance.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if a secondary already has an events-available callback set.
	/// </exception>
	public CompositeHostEventSource(
		IHostEventSource primary,
		params ReadOnlySpan<INotifyingHostEventSource> secondaries
	) {
		ArgumentNullException.ThrowIfNull(primary);
		sources = new IHostEventSource[secondaries.Length + 1];
		sources[0] = primary;
		for (int i = 0; i < secondaries.Length; i++) {
			INotifyingHostEventSource s = secondaries[i] ?? throw new ArgumentNullException(nameof(secondaries));
			if (Array.IndexOf(sources, s, 0, i + 1) >= 0)
				throw new ArgumentException("a source appears more than once", nameof(secondaries));
			if (!ReferenceEquals(s.Clock, primary.Clock))
				throw new ArgumentException("all sources must share the primary's clock instance", nameof(secondaries));
			sources[i + 1] = s;
		}
		pending = new HostEvent[sources.Length];
		hasPending = new bool[sources.Length];

		wakePrimary = primary.Wake;
		int registered = 0;
		try {
			for (; registered < secondaries.Length; registered++)
				secondaries[registered].SetEventsAvailableCallback(wakePrimary);
		} catch {
			for (int i = 0; i < registered; i++)
				secondaries[i].SetEventsAvailableCallback(null);
			throw;
		}
	}

	/// <inheritdoc/>
	public IHostClock Clock => sources[0].Clock;

	/// <inheritdoc/>
	public HostDuration WaitGranularity => sources[0].WaitGranularity;

	/// <inheritdoc/>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this composite has been disposed.
	/// </exception>
	/// <remarks>
	/// Keeps one pending event per source and returns the one with the earliest tick; if there is a
	/// tie, the primary is preferred followed by the secondaries in order. A source that only later
	/// delivers an event with an earlier tick still gets it returned later.
	/// </remarks>
	public bool TryPoll(out HostEvent ev) {
		ObjectDisposedException.ThrowIf(disposed, this);
		int best = -1;
		for (int i = 0; i < sources.Length; i++) {
			if (!hasPending[i])
				hasPending[i] = sources[i].TryPoll(out pending[i]);
			if (hasPending[i] && (best < 0 || pending[i].Tick < pending[best].Tick))
				best = i;
		}
		if (best < 0) {
			ev = default;
			return false;
		}
		ev = pending[best];
		hasPending[best] = false;
		pending[best] = default;
		return true;
	}

	/// <inheritdoc/>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this composite has been disposed.
	/// </exception>
	public bool WaitUntil(HostTick deadline) {
		ObjectDisposedException.ThrowIf(disposed, this);
		// a secondary event arriving between this check and the primary's wait still wakes it, since
		// Wake applies to the next wait too
		if (anyPending())
			return true;
		return sources[0].WaitUntil(deadline);
	}

	/// <inheritdoc/>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this composite has been disposed.
	/// </exception>
	public void WaitIndefinitely() {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (!anyPending())
			sources[0].WaitIndefinitely();
	}

	/// <inheritdoc/>
	public void Wake() => sources[0].Wake();

	/// <summary>
	/// Unregisters from the secondaries. Still-buffered events are dropped.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		for (int i = 1; i < sources.Length; i++)
			((INotifyingHostEventSource)sources[i]).SetEventsAvailableCallback(null);
	}

	private bool anyPending() {
		for (int i = 0; i < sources.Length; i++) {
			if (!hasPending[i])
				hasPending[i] = sources[i].TryPoll(out pending[i]);
			if (hasPending[i])
				return true;
		}
		return false;
	}
}
