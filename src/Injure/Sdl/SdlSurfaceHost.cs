// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Hexa.NET.SDL3;
using Injure.Gpu;

namespace Injure.Sdl;

internal sealed unsafe class SdlSurfaceHost(SdlWindow window) : ISurfaceHost {
	private readonly SdlWindow window = window;

	private const string drvCocoa = "cocoa";
	private const string drvWayland = "wayland";
	private const string drvWindows = "windows";
	private const string drvX11 = "x11";

	public SurfaceSource GetSurfaceSource() {
		SDLWindow* win = window.DangerousGetHandle();
		uint props = SDL.GetWindowProperties(win);
		string drv = SDL.GetCurrentVideoDriverS();
		switch (drv) {
		case drvCocoa: {
			void* metalLayer = window.DangerousGetMetalLayer();
			if (metalLayer is null)
				throw new InternalStateException("cocoa video driver but no Metal layer");
			return SurfaceSource.DangerousCreateFromMetalLayer((nint)metalLayer);
		}
		case drvWayland:
			return SurfaceSource.DangerousCreateFromWaylandSurface(
				(nint)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WAYLAND_DISPLAY_POINTER, null),
				(nint)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WAYLAND_SURFACE_POINTER, null)
			);
		case drvWindows:
			return SurfaceSource.DangerousCreateFromWindowsHwnd(
				(nint)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WIN32_HWND_POINTER, null),
				(nint)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_WIN32_INSTANCE_POINTER, null)
			);
		case drvX11:
			return SurfaceSource.DangerousCreateFromXlibWindow(
				(nint)SDL.GetPointerProperty(props, SDL.SDL_PROP_WINDOW_X11_DISPLAY_POINTER, null),
				(ulong)SDL.GetNumberProperty(props, SDL.SDL_PROP_WINDOW_X11_WINDOW_NUMBER, 0)
			);
		default:
			throw new PlatformNotSupportedException($"unsupported SDL videodriver '{drv}'");
		}
	}

	public (uint Width, uint Height) GetDrawableSize() {
		int w, h;
		SdlException.Check(SDL.GetWindowSizeInPixels(window.DangerousGetHandle(), &w, &h));
		if (w < 0 || h < 0)
			throw new InvalidOperationException("SDL_GetWindowSizeInPixels returned negative size");
		return ((uint)w, (uint)h);
	}
}
