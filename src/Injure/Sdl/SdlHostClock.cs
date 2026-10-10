// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using SDL3;
using Injure.Host;

namespace Injure.Sdl;

/// <summary>
/// An <see cref="IHostClock"/> backed by <c>SDL_GetTicksNS</c>, i.e. the same clock SDL stamps
/// events with.
/// </summary>
/// <remarks>
/// <para>
/// Obtained from <see cref="SdlInstance.Clock"/>. The epoch is the moment SDL was initialized.
/// </para>
/// <para>
/// Usable from any thread. Once the owning <see cref="SdlInstance"/> is disposed, <see cref="Now"/>
/// throws <see cref="ObjectDisposedException"/>, since a re-inited SDL would start counting from a
/// new epoch and silently produce values that aren't comparable with earlier ones. A call racing
/// with the disposal either finishes before SDL is shut down or throws; disposal waits for
/// in-flight calls before shutting SDL down.
/// </para>
/// </remarks>
public sealed class SdlHostClock : IHostClock {
	private int valid = 1;
	private int inFlight = 0; // Now calls currently past the validity check

	internal SdlHostClock() {
	}

	/// <inheritdoc/>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the owning <see cref="SdlInstance"/> has been disposed.
	/// </exception>
	public HostTick Now {
		get {
			// SDL_GetTicksNS lazily reinitializes SDL's (non-atomic) tick state if it runs after SDL_Quit,
			// so a call must never be in flight once the instance starts shutting down
			//
			// Interlocked.Increment is a full fence so either this sees valid == 0 or Invalidate sees
			// inFlight != 0
			_ = Interlocked.Increment(ref inFlight);
			try {
				checkValid();
				return HostTick.DangerousCreateFromRaw(SDL.GetTicksNS());
			} finally {
				_ = Interlocked.Decrement(ref inFlight);
			}
		}
	}

	/// <summary>
	/// Converts an SDL event timestamp (e.g. <c>SDL_KeyboardEvent.timestamp</c>) to a
	/// <see cref="HostTick"/> on this clock.
	/// </summary>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the owning <see cref="SdlInstance"/> has been disposed.
	/// </exception>
	/// <remarks>
	/// Currently just a clearer-intent wrapper for
	/// <see cref="HostTick.DangerousCreateFromRaw(ulong)"/>.
	/// </remarks>
	public HostTick FromSdlTimestamp(ulong timestampNs) {
		checkValid();
		return HostTick.DangerousCreateFromRaw(timestampNs);
	}

	/// <remarks>
	/// Called by <see cref="SdlInstance.Dispose()"/> right before <c>SDL_Quit</c>; returns once no
	/// <see cref="Now"/> call can still reach <c>SDL_GetTicksNS</c>.
	/// </remarks>
	internal void Invalidate() {
		// force a full fence, as the store to valid must not be reordered after the load of inFlight
		_ = Interlocked.Exchange(ref valid, 0);
		SpinWait sw = new();
		while (Volatile.Read(ref inFlight) != 0)
			sw.SpinOnce();
	}

	private void checkValid() {
		if (Volatile.Read(ref valid) == 0)
			throw new ObjectDisposedException(nameof(SdlHostClock), "the SdlInstance this clock belongs to has been disposed");
	}
}
