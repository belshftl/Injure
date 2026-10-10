// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Draw;
using Injure.Gpu;
using Injure.Host;
using Injure.Primitives;
using Injure.Sdl;
using SDL3;

namespace WaylandEmbed;

internal static partial class Native {
	private const string lib = "wlembed";

	[LibraryImport(lib)]
	public static partial nint wlembed_sub_create(nint display, nint parent);

	[LibraryImport(lib)]
	public static partial void wlembed_sub_destroy(nint sub);

	[LibraryImport(lib)]
	public static partial nint wlembed_sub_surface(nint sub);

	[LibraryImport(lib)]
	public static partial void wlembed_sub_configure(nint sub, int x, int y, int scale);

	[LibraryImport(lib)]
	public static partial int wlembed_sub_dispatch(nint sub);
}

/// <summary>
/// A <c>wl_subsurface</c> covering the right half of the window, standing in for a native child
/// view from a hypothetical UI framework.
/// </summary>
internal sealed class ChildView : ISurfaceHost, IDisposable {
	private readonly nint display;
	private readonly SdlWindow window;
	private nint sub;
	private (int X, int Scale) configured;
	private uint width, height;

	public ChildView(nint display, nint parentSurface, SdlWindow window) {
		this.display = display;
		this.window = window;
		sub = Native.wlembed_sub_create(display, parentSurface);
		if (sub == 0)
			throw new InvalidOperationException("couldn't create the subsurface");
		Layout();
	}

	// follows the window's size; called every frame before rendering
	public void Layout() {
		SdlWindowState st = window.State;
		int scale = Math.Max(1, (int)Math.Round((double)st.PixelWidth / st.Width));
		int x = st.Width / 2;
		if ((x, scale) != configured) {
			Native.wlembed_sub_configure(sub, x, 0, scale);
			configured = (x, scale);
		}
		width = (uint)((st.Width - x) * scale);
		height = (uint)(st.Height * scale);
	}

	public void Dispatch() {
		if (Native.wlembed_sub_dispatch(sub) < 0)
			throw new InvalidOperationException("wl_display_dispatch_queue_pending failed");
	}

	public SurfaceSource GetSurfaceSource() =>
		sub != 0
			? SurfaceSource.DangerousCreateFromWaylandSurface(display, Native.wlembed_sub_surface(sub))
			: throw new InvalidOperationException("the subsurface was destroyed");

	public (uint Width, uint Height) GetDrawableSize() => (width, height);

	public void Dispose() {
		Native.wlembed_sub_destroy(sub);
		sub = 0;
	}
}

public static class Program {
	public static int Main(string[] args) {
		string title = "Injure in a Wayland subsurface";
		for (int i = 0; i < args.Length; i++) {
			if (args[i] == "--title" && i + 1 < args.Length) {
				title = args[++i];
			} else {
				Console.Error.WriteLine("usage: WaylandEmbed [--title <window title>]");
				return 2;
			}
		}
		double seconds = double.TryParse(Environment.GetEnvironmentVariable("INJURE_SMOKE_SECONDS"), out double s) ? s : double.PositiveInfinity;

		using var sdl = SdlInstance.Init(new SdlInitOptions { VideoDriver = "wayland" });
		using var window = SdlWindow.Create(sdl, new SdlWindowOptions {
			Title = title,
			Width = 960,
			Height = 540,
		});
		uint props = SDL.GetWindowProperties(window.DangerousGetHandle());
		nint display = SDL.GetPointerProperty(props, SDL.Props.WindowWaylandDisplayPointer, 0);
		nint parentSurface = SDL.GetPointerProperty(props, SDL.Props.WindowWaylandSurfacePointer, 0);
		if (display == 0 || parentSurface == 0)
			throw new InvalidOperationException("SDL isn't using its Wayland driver");

		using GpuDevice device = new(new GpuDeviceOptions {
			RequiredFeatures = GpuFeatures.None,
			CompatibleHost = window.SurfaceHost,
			ErrorHandler = GpuErrorHandlers.Log(Console.Error),
		});
		EngineResourceStore engineResources = new();
		engineResources.RegisterSource(new EmbeddedEngineResourceSource(typeof(Canvas).Assembly, [
			BuiltinShaders.Primitive2d.ResourceId,
			BuiltinShaders.Textured2dColor.ResourceId,
			BuiltinShaders.Textured2dRmask.ResourceId,
			BuiltinShaders.Textured2dSdf.ResourceId,
		]));
		using CanvasSharedResources canvasResources = new(device, engineResources);
		using ViewGlobals parentGlobals = new(device, 1, 1);
		using ViewGlobals childGlobals = new(device, 1, 1);
		using SurfaceRenderOutput parentOutput = new(device, window.SurfaceHost, SurfaceFormatPolicy.PreferNonSrgb, SurfacePresentModePolicy.AutoMailbox);

		ChildView? child = null;
		SurfaceRenderOutput? childOutput = null;
		void createChild() {
			child = new ChildView(display, parentSurface, window);
			childOutput = new SurfaceRenderOutput(device, child, SurfaceFormatPolicy.PreferNonSrgb, SurfacePresentModePolicy.AutoMailbox);
		}
		void destroyChild() {
			childOutput?.Dispose();
			childOutput = null;
			child?.Dispose();
			child = null;
		}

		var sw = Stopwatch.StartNew();
		bool render(IRenderOutput output, ViewGlobals globals, SrgbColor32 clear, bool leftHalfOnly) {
			if (!RenderFrame.TryBegin(device, output, out RenderFrame? frame))
				return false;
			using (frame) {
				uint w = frame.PrimaryView.Width, h = frame.PrimaryView.Height;
				globals.Update(w, h);
				CanvasParams baseParams = new(
					Target: CanvasTarget.Primary,
					ColorAttachmentOps: ColorAttachmentOps.Clear(clear.ToRawF128()),
					Scissor: CanvasScissor.None,
					Transform: Matrix3x2.Identity,
					OutputState: CanvasOutputStates.Alpha,
					Material: CanvasMaterials.Color
				);
				using (Canvas cv = new(device, globals, frame, canvasResources, in baseParams)) {
					float t = (float)sw.Elapsed.TotalSeconds;
					float regionW = leftHalfOnly ? w / 2f : w;
					Vector2 c = new(regionW / 2f, h / 2f);
					float r = Math.Min(c.X, c.Y) * 0.8f;
					Vector2 at(float a) => c + r * new Vector2(MathF.Cos(t + a), MathF.Sin(t + a));
					cv.Triangle(
						at(0f),
						at(2f * MathF.PI / 3f),
						at(4f * MathF.PI / 3f),
						new SrgbColor32(0x50, 0xc8, 0x78)
					);
				}
				frame.Submit();
			}
			return true;
		}

		int parentFrames = 0, childFrames = 0;
		bool resized = false, detached = false, reattached = false;
		try {
			createChild();
			for (bool running = true; running;) {
				while (sdl.Events.TryPoll(out HostEvent ev))
					if (ev.Kind == HostEventKind.Quit || ev.Kind == HostEventKind.WindowCloseRequested)
						running = false;

				double now = sw.Elapsed.TotalSeconds;
				if (!resized && now >= seconds / 2) {
					resized = true;
					SdlWindowState st = window.State;
					window.RequestSize(st.Width + 160, st.Height + 90);
					Console.WriteLine($"resized at child frame {childFrames}");
				}
				if (!detached && now >= seconds * 0.6) {
					detached = true;
					destroyChild();
					Console.WriteLine($"destroyed the subsurface at parent frame {parentFrames}");
				}
				if (!reattached && now >= seconds * 0.6 + seconds / 8) {
					reattached = true;
					createChild();
					Console.WriteLine($"recreated the subsurface at parent frame {parentFrames}");
				}
				if (now >= seconds)
					running = false;

				device.Poll();
				if (child is not null && childOutput is not null) {
					child.Dispatch();
					child.Layout();
					if (render(childOutput, childGlobals, SrgbColor32.Black, leftHalfOnly: false))
						childFrames++;
				}
				// presenting the parent also commits the subsurface's position
				if (render(parentOutput, parentGlobals, new SrgbColor32(0x1e, 0x1e, 0x28), leftHalfOnly: true))
					parentFrames++;
			}
		} finally {
			destroyChild();
		}

		Console.WriteLine(
			$"parent {parentOutput.Format} {parentOutput.Width}x{parentOutput.Height} frames: {parentFrames}, child frames: {childFrames}"
		);
		return 0;
	}
}
