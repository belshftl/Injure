// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using SDL3;
using Injure.Gpu;

namespace Injure.Sdl;

internal sealed class SdlSurfaceHost(SdlWindow window) : ISurfaceHost {
	private readonly SdlWindow window = window;

	private const string drvCocoa = "cocoa";
	private const string drvWayland = "wayland";
	private const string drvWindows = "windows";
	private const string drvX11 = "x11";

	public SurfaceSource GetSurfaceSource() {
		uint props = SDL.GetWindowProperties(window.DangerousGetHandle());
		string drv = SDL.GetCurrentVideoDriver() ?? throw SdlException.FromLastError("SDL_GetCurrentVideoDriver");
		switch (drv) {
		case drvCocoa: {
			nint metalLayer = window.DangerousGetMetalLayer();
			if (metalLayer == 0)
				throw new InternalStateException("cocoa video driver but no Metal layer");
			return SurfaceSource.DangerousCreateFromMetalLayer(metalLayer);
		}
		case drvWayland:
			return SurfaceSource.DangerousCreateFromWaylandSurface(
				SDL.GetPointerProperty(props, SDL.Props.WindowWaylandDisplayPointer, 0),
				SDL.GetPointerProperty(props, SDL.Props.WindowWaylandSurfacePointer, 0)
			);
		case drvWindows:
			return SurfaceSource.DangerousCreateFromWindowsHwnd(
				SDL.GetPointerProperty(props, SDL.Props.WindowWin32HWNDPointer, 0),
				SDL.GetPointerProperty(props, SDL.Props.WindowWin32InstancePointer, 0)
			);
		case drvX11:
			return SurfaceSource.DangerousCreateFromXlibWindow(
				SDL.GetPointerProperty(props, SDL.Props.WindowX11DisplayPointer, 0),
				(ulong)SDL.GetNumberProperty(props, SDL.Props.WindowX11WindowNumber, 0)
			);
		default:
			throw new PlatformNotSupportedException($"unsupported SDL videodriver '{drv}'");
		}
	}

	public (uint Width, uint Height) GetDrawableSize() {
		SdlException.Check(SDL.GetWindowSizeInPixels(window.DangerousGetHandle(), out int w, out int h));
		if (w < 0 || h < 0)
			throw new SdlException("SDL_GetWindowSizeInPixels", "returned negative size");
		return ((uint)w, (uint)h);
	}
}
