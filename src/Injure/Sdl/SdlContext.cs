// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hexa.NET.SDL3;
using static Injure.Sdl.SdlException;

namespace Injure.Sdl;

/// <summary>
/// Options for <see cref="SdlContext.Init(in SdlInitOptions)"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and initializes SDL with its own default video
/// driver, no extra hints, and without the gamepad subsystem.
/// </remarks>
public readonly struct SdlInitOptions {
	/// <summary>
	/// Video driver to request through <c>SDL_HINT_VIDEO_DRIVER</c>, e.g. <c>"wayland"</c>, or
	/// <see langword="null"/> to let SDL pick.
	/// </summary>
	public string? VideoDriver { get; init; }

	/// <summary>
	/// Whether to also initialize the gamepad subsystem (<c>SDL_INIT_GAMEPAD</c>).
	/// </summary>
	public bool Gamepad { get; init; }

	/// <summary>
	/// Raw SDL hints (name to value) to set before SDL is initialized, or <see langword="null"/> for
	/// none.
	/// </summary>
	/// <remarks>
	/// Must not contain <c>SDL_HINT_VIDEO_DRIVER</c> if <see cref="VideoDriver"/> is set.
	/// </remarks>
	public IReadOnlyDictionary<string, string>? Hints { get; init; }
}

/// <summary>
/// Owns the process's SDL state: SDL initialization, hints, the event queue, and the
/// <see cref="SdlHostClock"/>.
/// </summary>
/// <remarks>
/// <para>
/// At most one <see cref="SdlContext"/> can be alive in a process at a time. It is bound to the
/// thread that created it: every SDL-touching member of it and of the objects created from it
/// (such as <see cref="SdlWindow"/>) throws <see cref="InvalidOperationException"/> when called
/// from any other thread. The exception is <see cref="Clock"/>, which is usable from any thread.
/// On macOS, SDL additionally requires that thread to be the process's main thread, and
/// <see cref="Init(in SdlInitOptions)"/> checks for it.
/// </para>
/// <para>
/// There is no finalizer, since SDL can't be cleaned up from the finalizer thread; if this is never
/// disposed, SDL stays up until the process exits.
/// </para>
/// </remarks>
public sealed partial class SdlContext : IDisposable {
	private static partial class MacNative {
		[LibraryImport("/usr/lib/libSystem.B.dylib")]
		[SupportedOSPlatform("macos")]
		public static partial int pthread_main_np();
	}

	private static int active = 0;

	private readonly int ownerThreadId;
	private readonly Dictionary<uint, SdlWindow> windows = new();
	private bool disposed;

	/// <summary>
	/// The clock that SDL event timestamps are expressed in.
	/// </summary>
	public SdlHostClock Clock { get; }

	/// <summary>
	/// SDL's event queue.
	/// </summary>
	public SdlEventSource Events { get; }

	private SdlContext() {
		ownerThreadId = Environment.CurrentManagedThreadId;
		Clock = new SdlHostClock();
		Events = new SdlEventSource(this);
	}

	/// <summary>
	/// Initializes SDL and returns the context that owns it.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="options"/> sets both <see cref="SdlInitOptions.VideoDriver"/> and
	/// a raw <c>SDL_HINT_VIDEO_DRIVER</c> hint.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if another <see cref="SdlContext"/> is alive, or if called from a thread other than the
	/// main thread on macOS.
	/// </exception>
	/// <exception cref="SdlException">
	/// Thrown if setting a hint or initializing SDL fails.
	/// </exception>
	public static SdlContext Init(in SdlInitOptions options = default) {
		if (options.VideoDriver is not null && options.Hints is not null && options.Hints.ContainsKey(SDL.SDL_HINT_VIDEO_DRIVER))
			throw new ArgumentException("VideoDriver and a raw SDL_HINT_VIDEO_DRIVER hint are both set", nameof(options));
		if (OperatingSystem.IsMacOS() && MacNative.pthread_main_np() == 0)
			throw new InvalidOperationException("on macOS, SDL must be initialized on the process's main thread");
		if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
			throw new InvalidOperationException("an SdlContext is already alive; only one can exist at a time");

		try {
			if (options.Hints is not null)
				foreach (KeyValuePair<string, string> hint in options.Hints)
					Check(SDL.SetHint(hint.Key, hint.Value));
			if (options.VideoDriver is not null)
				Check(SDL.SetHint(SDL.SDL_HINT_VIDEO_DRIVER, options.VideoDriver));
			uint flags = SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_EVENTS;
			if (options.Gamepad)
				flags |= SDL.SDL_INIT_GAMEPAD;
			Check(SDL.Init(flags));
		} catch {
			Volatile.Write(ref active, 0);
			throw;
		}
		try {
			return new SdlContext();
		} catch {
			SDL.Quit();
			Volatile.Write(ref active, 0);
			throw;
		}
	}

	/// <summary>
	/// Shuts SDL down.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created this context, or if any
	/// <see cref="SdlWindow"/> created from it is still alive.
	/// </exception>
	/// <remarks>
	/// After this, <see cref="Clock"/> throws <see cref="ObjectDisposedException"/>, since SDL
	/// restarts its tick counter on re-init.
	/// </remarks>
	public void Dispose() {
		checkThread();
		if (disposed)
			return;
		if (windows.Count != 0)
			throw new InvalidOperationException($"cannot dispose an SdlContext while {windows.Count} SdlWindow(s) created from it are still alive");
		disposed = true;
		Events.Shutdown();
		Clock.Invalidate();
		SDL.Quit();
		Volatile.Write(ref active, 0);
	}

	internal void CheckAccess() {
		checkThread();
		ObjectDisposedException.ThrowIf(disposed, this);
	}

	internal void RegisterWindow(uint sdlWindowId, SdlWindow window) => windows.Add(sdlWindowId, window);
	internal void UnregisterWindow(uint sdlWindowId) => windows.Remove(sdlWindowId);
	internal bool TryGetWindow(uint sdlWindowId, [NotNullWhen(true)] out SdlWindow? window) => windows.TryGetValue(sdlWindowId, out window);

	private void checkThread() {
		if (Environment.CurrentManagedThreadId != ownerThreadId)
			throw new InvalidOperationException("SDL objects can only be used from the thread that created the SdlContext");
	}
}
