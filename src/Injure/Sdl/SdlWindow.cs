// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using SDL3;
using Injure.Host;
using Injure.Gpu;
using static Injure.Sdl.SdlException;

namespace Injure.Sdl;

/// <summary>
/// An SDL window, plus the Metal view that WebGPU renders through on macOS.
/// </summary>
/// <remarks>
/// <para>
/// Bound to the thread of its <see cref="SdlInstance"/> like everything else SDL-related; see
/// <see cref="SdlInstance"/> for details. An instance can't be disposed while any of its windows are
/// alive.
/// </para>
/// <para>
/// Any WebGPU surface created through <see cref="SurfaceHost"/> must be released before the window
/// is disposed, since it refers to the native window. This is currently not enforced.
/// </para>
/// </remarks>
public sealed class SdlWindow : IDisposable {
	private nint handle; // SDL_Window*
	private nint metalView; // SDL_MetalView
	private nint metalLayer; // CAMetalLayer*
	private readonly uint sdlWindowId;
	private SdlWindowState state;

	/// <summary>
	/// The instance this window was created from.
	/// </summary>
	public SdlInstance Sdl { get; }

	/// <summary>
	/// This window's process-wide ID.
	/// </summary>
	public HostWindowId Id { get; }

	/// <summary>
	/// A surface host for creating a WebGPU surface on this window.
	/// </summary>
	public ISurfaceHost SurfaceHost { get; }

	/// <summary>
	/// Whether <see cref="Dispose()"/> has been called.
	/// </summary>
	public bool IsDisposed => handle == 0;

	/// <summary>
	/// The window's state as of the last processed window event or immediate setter call.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <remarks>
	/// Updated by <see cref="SdlEventSource"/> as it translates this window's events, so it lags
	/// behind <c>Request*</c> calls until their events have been polled; see
	/// <see cref="SdlWindowState"/>.
	/// </remarks>
	public SdlWindowState State {
		get {
			CheckAccess();
			return state;
		}
	}

	private SdlWindow(SdlInstance sdl, nint handle, nint metalView, nint metalLayer, uint sdlWindowId) {
		Sdl = sdl;
		this.handle = handle;
		this.metalView = metalView;
		this.metalLayer = metalLayer;
		this.sdlWindowId = sdlWindowId;
		Id = HostWindowId.Allocate();
		SurfaceHost = new SdlSurfaceHost(this);
		state = queryState(handle, sdl.Clock.Now);
	}

	/// <summary>
	/// Creates a window.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="sdl"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="options"/> is invalid; see <see cref="SdlWindowOptions"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <paramref name="sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if <paramref name="sdl"/> has been disposed.
	/// </exception>
	/// <exception cref="SdlException">
	/// Thrown if an SDL call fails.
	/// </exception>
	public static SdlWindow Create(SdlInstance sdl, in SdlWindowOptions options) {
		ArgumentNullException.ThrowIfNull(sdl);
		sdl.CheckAccess();
		options.Validate(nameof(options));

		uint props = SDL.CreateProperties();
		if (props == 0)
			throw FromLastError("SDL_CreateProperties");
		try {
			Check(SDL.SetStringProperty(props, SDL.Props.WindowCreateTitleString, options.Title));
			Check(SDL.SetNumberProperty(props, SDL.Props.WindowCreateWidthNumber, options.Width));
			Check(SDL.SetNumberProperty(props, SDL.Props.WindowCreateHeightNumber, options.Height));
			(int x, int y) = options.Position.ToSdl();
			Check(SDL.SetNumberProperty(props, SDL.Props.WindowCreateXNumber, x));
			Check(SDL.SetNumberProperty(props, SDL.Props.WindowCreateYNumber, y));
			Check(SDL.SetBooleanProperty(props, SDL.Props.WindowCreateHiddenBoolean, !options.Visible));
			Check(SDL.SetBooleanProperty(props, SDL.Props.WindowCreateResizableBoolean, options.Resizable));
			Check(SDL.SetBooleanProperty(props, SDL.Props.WindowCreateBorderlessBoolean, options.Borderless));
			Check(SDL.SetBooleanProperty(props, SDL.Props.WindowCreateFullscreenBoolean, options.Fullscreen));
			Check(SDL.SetBooleanProperty(props, SDL.Props.WindowCreateHighPixelDensityBoolean, options.HighPixelDensity));
			Check(SDL.SetBooleanProperty(props, SDL.Props.WindowCreateMinimizedBoolean, options.Mode == SdlWindowMode.Minimized));
			Check(SDL.SetBooleanProperty(props, SDL.Props.WindowCreateMaximizedBoolean, options.Mode == SdlWindowMode.Maximized));
			if (OperatingSystem.IsMacOS())
				Check(SDL.SetBooleanProperty(props, SDL.Props.WindowCreateMetalBoolean, true));
			return createFrom(sdl, props);
		} finally {
			SDL.DestroyProperties(props);
		}
	}

	/// <summary>
	/// Creates a window from raw <c>SDL_CreateWindowWithProperties</c> properties.
	/// </summary>
	/// <param name="sdl">Instance to create the window in.</param>
	/// <param name="props">
	/// A valid <c>SDL_PropertiesID</c>. Stays owned by the caller. Also see this method's remarks for
	/// contracts it must uphold.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="sdl"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if on macOS and <c>SDL_PROP_WINDOW_CREATE_METAL_BOOLEAN</c> is not set to true in
	/// <paramref name="props"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <paramref name="sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if <paramref name="sdl"/> has been disposed.
	/// </exception>
	/// <exception cref="SdlException">
	/// Thrown if an SDL call fails.
	/// </exception>
	/// <remarks>
	/// On macOS, <c>SDL_PROP_WINDOW_CREATE_METAL_BOOLEAN</c> <b>must</b> be set to true in
	/// <paramref name="props"/>.
	/// </remarks>
	public static SdlWindow DangerousCreateFromProperties(SdlInstance sdl, uint props) {
		ArgumentNullException.ThrowIfNull(sdl);
		sdl.CheckAccess();
		return createFrom(sdl, props);
	}

	private static SdlWindow createFrom(SdlInstance sdl, uint props) {
		if (
			OperatingSystem.IsMacOS()
			&& !SDL.GetBooleanProperty(props, SDL.Props.WindowCreateMetalBoolean, false)
		)
			throw new ArgumentException("on macOS, SDL_PROP_WINDOW_CREATE_METAL_BOOLEAN must be set to true");

		nint handle = SDL.CreateWindowWithProperties(props);
		if (handle == 0)
			throw FromLastError("SDL_CreateWindowWithProperties");

		nint metalView = 0;
		nint metalLayer = 0;
		try {
			uint sdlWindowId = SDL.GetWindowID(handle);
			if (sdlWindowId == 0)
				throw FromLastError("SDL_GetWindowID");
			if (OperatingSystem.IsMacOS()) {
				metalView = SDL.MetalCreateView(handle);
				if (metalView == 0)
					throw FromLastError("SDL_Metal_CreateView");
				metalLayer = SDL.MetalGetLayer(metalView);
				if (metalLayer == 0)
					throw FromLastError("SDL_Metal_GetLayer");
			}
			SdlWindow window = new(sdl, handle, metalView, metalLayer, sdlWindowId);
			sdl.RegisterWindow(sdlWindowId, window);
			return window;
		} catch {
			if (metalView != 0)
				SDL.MetalDestroyView(metalView);
			SDL.DestroyWindow(handle);
			throw;
		}
	}

	/// <summary>
	/// Gets the underlying <c>SDL_Window*</c>, bypassing ownership/lifetime. Dangles once destroyed by
	/// <see cref="Dispose()"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b> See
	/// <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public nint DangerousGetHandle() {
		CheckAccess();
		return handle;
	}

	/// <summary>
	/// Gets the <c>SDL_MetalView</c> created for this window on macOS, bypassing ownership/lifetime,
	/// or 0 on other platforms. Dangles once destroyed by
	/// <see cref="Dispose()"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b> See
	/// <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public nint DangerousGetMetalView() {
		CheckAccess();
		return metalView;
	}

	/// <summary>
	/// Gets the <c>CAMetalLayer*</c> of this window's Metal view on macOS, bypassing
	/// ownership/lifetime, or 0 on other platforms. Dangles once destroyed by
	/// <see cref="Dispose()"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b> See
	/// <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public nint DangerousGetMetalLayer() {
		CheckAccess();
		return metalLayer;
	}

	// ==========================================================================
	// immediate setters

	/// <summary>
	/// Sets the window title. Takes effect immediately.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="title"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <exception cref="SdlException">
	/// Thrown if the SDL call fails.
	/// </exception>
	public void SetTitle(string title) {
		ArgumentNullException.ThrowIfNull(title);
		CheckAccess();
		Check(SDL.SetWindowTitle(handle, title));
		state = state with { Title = title, UpdatedAt = Sdl.Clock.Now };
	}

	/// <summary>
	/// Sets whether the user can resize the window. Takes effect immediately.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <exception cref="SdlException">
	/// Thrown if the SDL call fails.
	/// </exception>
	public void SetResizable(bool resizable) {
		CheckAccess();
		Check(SDL.SetWindowResizable(handle, resizable));
		state = state with { Resizable = resizable, UpdatedAt = Sdl.Clock.Now };
	}

	/// <summary>
	/// Sets whether the window has window decorations. Takes effect immediately.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <exception cref="SdlException">
	/// Thrown if the SDL call fails.
	/// </exception>
	public void SetBorderless(bool borderless) {
		CheckAccess();
		Check(SDL.SetWindowBordered(handle, !borderless));
		state = state with { Borderless = borderless, UpdatedAt = Sdl.Clock.Now };
	}

	// ==========================================================================
	// requests

	/// <summary>
	/// Asks the window system to resize the window, in screen coordinates.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <paramref name="width"/> or <paramref name="height"/> is not positive.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <exception cref="SdlException">
	/// Thrown if the SDL call fails.
	/// </exception>
	/// <remarks>
	/// Like every <c>Request*</c> method here, this is only a request: the window system may apply it
	/// later, adjust it, or ignore it (e.g. for a maximized or fullscreen window). The outcome arrives
	/// later as window events, and <see cref="State"/> changes once they're polled. Use
	/// <see cref="TrySync()"/> to wait for pending requests.
	/// </remarks>
	public void RequestSize(int width, int height) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		CheckAccess();
		Check(SDL.SetWindowSize(handle, width, height));
	}

	/// <summary>
	/// Asks the window system to move the window.
	/// </summary>
	/// <inheritdoc cref="RequestVisible(bool)" path="/exception"/>
	/// <inheritdoc cref="RequestSize(int, int)" path="/remarks"/>
	public void RequestPosition(SdlWindowPosition position) {
		CheckAccess();
		(int x, int y) = position.ToSdl();
		Check(SDL.SetWindowPosition(handle, x, y));
	}

	/// <summary>
	/// Asks the window system to enter or leave fullscreen.
	/// </summary>
	/// <inheritdoc cref="RequestVisible(bool)" path="/exception"/>
	/// <inheritdoc cref="RequestSize(int, int)" path="/remarks"/>
	public void RequestFullscreen(bool fullscreen) {
		CheckAccess();
		Check(SDL.SetWindowFullscreen(handle, fullscreen));
	}

	/// <summary>
	/// Asks the window system to minimize, maximize, or restore the window.
	/// </summary>
	/// <inheritdoc cref="RequestVisible(bool)" path="/exception"/>
	/// <inheritdoc cref="RequestSize(int, int)" path="/remarks"/>
	public void RequestMode(SdlWindowMode mode) {
		CheckAccess();
		switch (mode.Tag) {
		case SdlWindowMode.Case.Normal:
			Check(SDL.RestoreWindow(handle));
			break;
		case SdlWindowMode.Case.Minimized:
			Check(SDL.MinimizeWindow(handle));
			break;
		case SdlWindowMode.Case.Maximized:
			Check(SDL.MaximizeWindow(handle));
			break;
		}
	}

	/// <summary>
	/// Asks the window system to show or hide the window.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <exception cref="SdlException">
	/// Thrown if the SDL call fails.
	/// </exception>
	/// <inheritdoc cref="RequestSize(int, int)" path="/remarks"/>
	public void RequestVisible(bool visible) {
		CheckAccess();
		if (visible)
			Check(SDL.ShowWindow(handle));
		else
			Check(SDL.HideWindow(handle));
	}

	/// <summary>
	/// Blocks until the window system has applied all pending <c>Request*</c> calls for this window
	/// or until SDL's timeout (<c>SDL_HINT_VIDEO_SYNC_TIMEOUT</c>) expires.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if all requests were applied; <see langword="false"/> on timeout.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if this window has been disposed.
	/// </exception>
	/// <remarks>
	/// The resulting window events are queued, not applied; <see cref="State"/> still only changes
	/// once they are polled.
	/// </remarks>
	public bool TrySync() {
		CheckAccess();
		return SDL.SyncWindow(handle);
	}

	// ==========================================================================
	// state tracking

	// called by SdlEventSource for every translated event that belongs to this window
	internal void ApplyEvent(in HostEvent ev) {
		SdlWindowState s = state;
		switch (ev.Kind.Tag) {
		case HostEventKind.Case.WindowShown: s = s with { Visible = true }; break;
		case HostEventKind.Case.WindowHidden: s = s with { Visible = false }; break;
		case HostEventKind.Case.WindowMoved: s = s with { X = ev.Position.X, Y = ev.Position.Y }; break;
		case HostEventKind.Case.WindowResized: s = s with { Width = ev.Size.Width, Height = ev.Size.Height }; break;
		case HostEventKind.Case.WindowPixelSizeChanged: s = s with { PixelWidth = ev.Size.Width, PixelHeight = ev.Size.Height }; break;
		case HostEventKind.Case.WindowDisplayScaleChanged: s = s with { DisplayScale = ev.Scale }; break;
		case HostEventKind.Case.WindowMinimized: s = s with { Mode = SdlWindowMode.Minimized }; break;
		case HostEventKind.Case.WindowMaximized: s = s with { Mode = SdlWindowMode.Maximized }; break;
		case HostEventKind.Case.WindowRestored: s = s with { Mode = SdlWindowMode.Normal }; break;
		case HostEventKind.Case.WindowEnteredFullscreen: s = s with { Fullscreen = true }; break;
		case HostEventKind.Case.WindowLeftFullscreen: s = s with { Fullscreen = false }; break;
		case HostEventKind.Case.WindowFocusGained: s = s with { HasKeyboardFocus = true }; break;
		case HostEventKind.Case.WindowFocusLost: s = s with { HasKeyboardFocus = false }; break;
		case HostEventKind.Case.WindowPointerEntered: s = s with { HasPointer = true }; break;
		case HostEventKind.Case.WindowPointerLeft: s = s with { HasPointer = false }; break;
		default: return;
		}
		state = s with { UpdatedAt = ev.Tick };
	}

	private static SdlWindowState queryState(nint handle, HostTick now) {
		Check(SDL.GetWindowSize(handle, out int width, out int height));
		Check(SDL.GetWindowSizeInPixels(handle, out int pixelWidth, out int pixelHeight));
		Check(SDL.GetWindowPosition(handle, out int x, out int y));
		SDL.WindowFlags flags = SDL.GetWindowFlags(handle);
		return new SdlWindowState {
			Title = SDL.GetWindowTitle(handle) ?? "",
			Width = width,
			Height = height,
			PixelWidth = pixelWidth,
			PixelHeight = pixelHeight,
			X = x,
			Y = y,
			Visible = (flags & SDL.WindowFlags.Hidden) == 0,
			Resizable = (flags & SDL.WindowFlags.Resizable) != 0,
			Borderless = (flags & SDL.WindowFlags.Borderless) != 0,
			Fullscreen = (flags & SDL.WindowFlags.Fullscreen) != 0,
			Mode =
				(flags & SDL.WindowFlags.Minimized) != 0 ? SdlWindowMode.Minimized
				: (flags & SDL.WindowFlags.Maximized) != 0 ? SdlWindowMode.Maximized
				: SdlWindowMode.Normal,
			DisplayScale = SDL.GetWindowDisplayScale(handle),
			HasKeyboardFocus = (flags & SDL.WindowFlags.InputFocus) != 0,
			HasPointer = (flags & SDL.WindowFlags.MouseFocus) != 0,
			UpdatedAt = now,
		};
	}

	/// <summary>
	/// Destroys the window.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if called from a thread other than the one that created <see cref="Sdl"/>.
	/// </exception>
	public void Dispose() {
		if (handle == 0)
			return;
		// the instance can't be disposed while this window is alive, so only the thread check can fail
		Sdl.CheckAccess();
		if (metalView != 0)
			SDL.MetalDestroyView(metalView);
		SDL.DestroyWindow(handle);
		Sdl.UnregisterWindow(sdlWindowId);
		handle = 0;
		metalView = 0;
		metalLayer = 0;
	}

	internal void CheckAccess() {
		Sdl.CheckAccess();
		ObjectDisposedException.ThrowIf(handle == 0, this);
	}
}
