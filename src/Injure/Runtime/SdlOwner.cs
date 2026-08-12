// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using Hexa.NET.SDL3;
using Injure.Time;
using static Injure.Runtime.SdlException;

namespace Injure.Runtime;

public static unsafe class SdlOwner {
	public static SDLWindow* Window { get; private set; }
	public static void* AppleMetalView { get; private set; } = null;
	public static void* AppleMetalLayer { get; private set; } = null;

	public static SdlSurfaceHost? SurfaceHost { get; private set; }

	private static bool inited = false;

	[MemberNotNull(nameof(Window), nameof(SurfaceHost))]
	public static void InitSdl(uint props) {
		if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null)
			Check(SDL.SetHint(SDL.SDL_HINT_VIDEO_DRIVER, "wayland"));

		Check(SDL.Init(SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_EVENTS));

		if (OperatingSystem.IsMacOS())
			Check(SDL.SetBooleanProperty(props, SDL.SDL_PROP_WINDOW_CREATE_METAL_BOOLEAN, true));

		Window = SDL.CreateWindowWithProperties(props);
		if (Window is null) {
			SDL.Quit();
			throw new SdlException("SDL_CreateWindow", SDL.GetErrorS());
		}

		if (OperatingSystem.IsMacOS()) {
			AppleMetalView = SDL.MetalCreateView(Window);
			if (AppleMetalView is null) {
				SDL.DestroyWindow(Window);
				SDL.Quit();
				throw new SdlException("SDL_Metal_CreateView", "SDL call returned null");
			}

			AppleMetalLayer = SDL.MetalGetLayer(AppleMetalView);
			if (AppleMetalLayer is null) {
				SDL.MetalDestroyView(AppleMetalView);
				SDL.DestroyWindow(Window);
				SDL.Quit();
				throw new SdlException("SDL_Metal_GetLayer", "SDL call returned null");
			}
		}

		SurfaceHost = new SdlSurfaceHost(Window, AppleMetalLayer);
		inited = true;
	}

	public static void ShutdownSdl() {
		if (inited) {
			SurfaceHost = null;
			if (AppleMetalView is not null) {
				SDL.MetalDestroyView(AppleMetalView);
				AppleMetalLayer = null;
				AppleMetalView = null;
			}
			SDL.DestroyWindow(Window);
			Window = null;
			SDL.Quit();
		}
	}

	internal static MonoTick MonoTickGetCurrent() {
		if (!inited)
			throw new InvalidOperationException("SDLOwner.InitSDL() not called yet");
		return (MonoTick)SDL.GetTicksNS();
	}
}
