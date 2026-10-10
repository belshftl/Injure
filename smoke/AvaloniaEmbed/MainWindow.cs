// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Layout;
using Injure.Assets;
using Injure.Assets.Builtin;
using Injure.Draw;
using Injure.Gpu;
using Injure.Primitives;
using Canvas = Injure.Draw.Canvas;

namespace AvaloniaEmbed;

public sealed class MainWindow : Window {
	private readonly GpuView gpuView = new();
	private readonly Image image = new() { Stretch = Avalonia.Media.Stretch.Fill };
	private readonly TextBlock status = new() { Margin = new Avalonia.Thickness(8) };
	private readonly Stopwatch sw = Stopwatch.StartNew();

	private GpuDevice? device;
	private SurfaceRenderOutput? surface;
	private OffscreenBitmapOutput? offscreen;
	private CanvasSharedResources? canvasResources;
	private ViewGlobals? surfaceGlobals, offscreenGlobals;
	private int surfaceFrames, offscreenFrames, offscreenSkipped;
	private bool loopRunning;

	public MainWindow(string title) {
		Title = title;
		Width = 960;
		Height = 540;

		Grid grid = new() {
			ColumnDefinitions = new ColumnDefinitions("*,*"),
			RowDefinitions = new RowDefinitions("Auto,*,Auto"),
		};
		grid.Children.Add(label($"NativeControlHost ({GpuView.NativeKind}) -> SurfaceRenderOutput", 0));
		grid.Children.Add(label("offscreen IRenderOutput -> readback -> WriteableBitmap", 1));
		Grid.SetRow(gpuView, 1);
		Grid.SetRow(image, 1);
		Grid.SetColumn(image, 1);
		Grid.SetRow(status, 2);
		Grid.SetColumnSpan(status, 2);
		grid.Children.Add(gpuView);
		grid.Children.Add(image);
		grid.Children.Add(status);
		Content = grid;

		gpuView.SurfaceReady += onSurfaceReady;
		gpuView.SurfaceLost += _ => {
			surface?.Dispose();
			surface = null;
		};
		Closed += (_, _) => shutdown();

		// unattended runs: close after INJURE_SMOKE_SECONDS and print the final status
		if (double.TryParse(Environment.GetEnvironmentVariable("INJURE_SMOKE_SECONDS"), out double seconds)) {
			// resize halfway through, to exercise the surface reconfiguring and the offscreen output reallocating
			Avalonia.Threading.DispatcherTimer.RunOnce(() => {
				Width += 160;
				Height += 90;
				Console.WriteLine($"resized at surface frame {surfaceFrames}");
			}, TimeSpan.FromSeconds(seconds / 2));

			// detach and reattach the native view, which destroys and recreates it (and its layer)
			Avalonia.Threading.DispatcherTimer.RunOnce(() => {
				var grid = (Grid)Content;
				grid.Children.Remove(gpuView);
				Console.WriteLine($"detached at surface frame {surfaceFrames}");
				Avalonia.Threading.DispatcherTimer.RunOnce(() => {
					grid.Children.Add(gpuView);
					Console.WriteLine($"reattached at surface frame {surfaceFrames}");
				}, TimeSpan.FromSeconds(seconds / 8));
			}, TimeSpan.FromSeconds(seconds * 0.6));
		}
	}

	private static TextBlock label(string text, int column) {
		TextBlock t = new() {
			Text = text,
			Margin = new Avalonia.Thickness(8),
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		Grid.SetColumn(t, column);
		return t;
	}

	private void onSurfaceReady(GpuView view) {
		device ??= createDevice(view);
		surface = new SurfaceRenderOutput(device, view, SurfaceFormatPolicy.PreferNonSrgb, SurfacePresentModePolicy.AutoMailbox);
		if (!loopRunning) {
			loopRunning = true;
			RequestAnimationFrame(onFrame);
		}
	}

	private GpuDevice createDevice(GpuView view) {
		GpuDevice dev = new(new GpuDeviceOptions {
			RequiredFeatures = GpuFeatures.None,
			CompatibleHost = view,
			ErrorHandler = GpuErrorHandlers.Log(Console.Error),
		});
		EngineResourceStore engineResources = new();
		engineResources.RegisterSource(new EmbeddedEngineResourceSource(typeof(Canvas).Assembly, [
			BuiltinShaders.Primitive2d.ResourceId,
			BuiltinShaders.Textured2dColor.ResourceId,
			BuiltinShaders.Textured2dRmask.ResourceId,
			BuiltinShaders.Textured2dSdf.ResourceId,
		]));
		canvasResources = new CanvasSharedResources(dev, engineResources);
		surfaceGlobals = new ViewGlobals(dev, 1, 1);
		offscreenGlobals = new ViewGlobals(dev, 1, 1);
		offscreen = new OffscreenBitmapOutput(dev, 1, 1);
		offscreen.FramePresented += () => {
			offscreenFrames++;
			image.InvalidateVisual();
		};
		return dev;
	}

	private void onFrame(TimeSpan _time) {
		if (device is null)
			return;
		device.Poll();

		if (surface is not null)
			if (render(surface, surfaceGlobals!, SrgbColor32.Black))
				surfaceFrames++;

		double scale = RenderScaling;
		offscreen!.ResizeTo((uint)Math.Max(1, image.Bounds.Width * scale), (uint)Math.Max(1, image.Bounds.Height * scale));
		image.Source = offscreen.Bitmap;
		if (!render(offscreen, offscreenGlobals!, new SrgbColor32(0x1e, 0x1e, 0x28)))
			offscreenSkipped++;

		status.Text = $"surface {surface?.Format} {surface?.Width}x{surface?.Height} presented {surfaceFrames}, offscreen {offscreen.Width}x{offscreen.Height} presented {offscreenFrames} skipped {offscreenSkipped}";
		RequestAnimationFrame(onFrame);
	}

	private bool render(IRenderOutput output, ViewGlobals globals, SrgbColor32 clear) {
		if (!RenderFrame.TryBegin(device!, output, out RenderFrame? frame))
			return false;
		using (frame) {
			globals.Update(frame.PrimaryView.Width, frame.PrimaryView.Height);
			CanvasParams baseParams = new(
				Target: CanvasTarget.Primary,
				ColorAttachmentOps: ColorAttachmentOps.Clear(clear.ToRawF128()),
				Scissor: CanvasScissor.None,
				Transform: Matrix3x2.Identity,
				OutputState: CanvasOutputStates.Alpha,
				Material: CanvasMaterials.Color
			);
			using (Canvas cv = new(device!, globals, frame, canvasResources!, in baseParams)) {
				float t = (float)sw.Elapsed.TotalSeconds;
				Vector2 c = new(frame.PrimaryView.Width / 2f, frame.PrimaryView.Height / 2f);
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

	private void shutdown() {
		surface?.Dispose();
		offscreen?.Dispose();
		canvasResources?.Dispose();
		surfaceGlobals?.Dispose();
		offscreenGlobals?.Dispose();
		device?.Dispose();
		device = null;
	}
}
