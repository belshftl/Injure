// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Injure.Gpu;

namespace AvaloniaEmbed;

/// <summary>
/// <c>NSView</c> + <c>CAMetalLayer</c> on macOS; X11 child window on, well, X11.
/// </summary>
public sealed partial class GpuView : NativeControlHost, ISurfaceHost {
	private static partial class Xlib {
		[LibraryImport("libX11.so.6")]
		public static partial nint XOpenDisplay(nint name);
	}

	// avalonia's connection to the X server isn't public, keep around our own
	private static nint x11Display;

	private MacosMetalLayer? metalLayer;
	private ulong x11Window;

	public static string NativeKind =>
		OperatingSystem.IsMacOS()
			? "CAMetalLayer"
			: OperatingSystem.IsLinux()
				? "X11 child window"
				: "<unsupported>";

	public event Action<GpuView>? SurfaceReady;
	public event Action<GpuView>? SurfaceLost;

	public SurfaceSource GetSurfaceSource() {
		if (OperatingSystem.IsMacOS() && metalLayer is not null)
			return metalLayer.GetSurfaceSource();
		if (x11Window != 0)
			return SurfaceSource.DangerousCreateFromXlibWindow(x11Display, x11Window);
		throw new InvalidOperationException("the native view doesn't exist");
	}

	public (uint Width, uint Height) GetDrawableSize() {
		double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
		return ((uint)Math.Max(1, Math.Round(Bounds.Width * scale)), (uint)Math.Max(1, Math.Round(Bounds.Height * scale)));
	}

	protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) {
		IPlatformHandle handle = base.CreateNativeControlCore(parent);
		switch (handle.HandleDescriptor) {
		case "NSView" when OperatingSystem.IsMacOS():
			metalLayer = MacosMetalLayer.DangerousCreateForView(handle.Handle);
			updateScale();
			break;
		case "XID":
			if (x11Display == 0) {
				x11Display = Xlib.XOpenDisplay(0);
				if (x11Display == 0)
					throw new InvalidOperationException("XOpenDisplay failed");
			}
			x11Window = (ulong)handle.Handle;
			break;
		default:
			throw new PlatformNotSupportedException($"unsupported native control handle '{handle.HandleDescriptor}'");
		}
		SurfaceReady?.Invoke(this);
		return handle;
	}

	protected override void DestroyNativeControlCore(IPlatformHandle control) {
		// the surface has to be destroyed before the native object it was created on
		SurfaceLost?.Invoke(this);
		if (OperatingSystem.IsMacOS())
			metalLayer?.Dispose();
		metalLayer = null;
		x11Window = 0;
		base.DestroyNativeControlCore(control);
	}

	private void updateScale() {
		if (OperatingSystem.IsMacOS() && metalLayer is not null)
			metalLayer.ContentsScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
		base.OnPropertyChanged(change);
		if (change.Property == BoundsProperty)
			updateScale();
	}
}
