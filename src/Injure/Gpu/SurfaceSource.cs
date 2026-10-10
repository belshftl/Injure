// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Injure.DevAnalyzers.Attributes;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The kind of a <see cref="SurfaceSource"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct SurfaceSourceKind {
	/// <summary>Raw switch tag for <see cref="SurfaceSourceKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// A <c>CAMetalLayer</c> (macOS, iOS).
		/// </summary>
		MetalLayer = 1,

		/// <summary>
		/// A Wayland <c>wl_surface</c> on a <c>wl_display</c>.
		/// </summary>
		WaylandSurface,

		/// <summary>
		/// An Xlib <c>Window</c> on a <c>Display</c>.
		/// </summary>
		XlibWindow,

		/// <summary>
		/// A Win32 <c>HWND</c> and the <c>HINSTANCE</c> of the module that created it.
		/// </summary>
		WindowsHwnd,
	}
}

/// <summary>
/// The native object a WebGPU surface is created on, as raw platform handles.
/// </summary>
/// <remarks>
/// <para>
/// Only constructible through the <c>DangerousCreateFrom*</c> factories, which can't check the
/// handles beyond rejecting null ones; see <c>docs/conventions/dangerous-get-create.md</c>. The
/// handles must stay valid for as long as any surface created from them is alive.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
public readonly struct SurfaceSource {
	private readonly nint handle0;
	private readonly nint handle1;
	private readonly ulong xlibWindow;

	/// <summary>
	/// The kind of native object.
	/// </summary>
	public SurfaceSourceKind Kind { get; }

	private SurfaceSource(SurfaceSourceKind kind, nint handle0, nint handle1, ulong xlibWindow) {
		Kind = kind;
		this.handle0 = handle0;
		this.handle1 = handle1;
		this.xlibWindow = xlibWindow;
	}

	/// <summary>
	/// Creates a source for a <c>CAMetalLayer</c>.
	/// </summary>
	/// <param name="layer">The <c>CAMetalLayer*</c>.</param>
	/// <remarks>
	/// The layer's <c>contentsScale</c> has to match the pixels per point of the view showing it,
	/// or macOS scales the presented image. To create a layer for an existing <c>NSView</c>, see
	/// <see cref="MacosMetalLayer"/>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="layer"/> is null.
	/// </exception>
	public static SurfaceSource DangerousCreateFromMetalLayer(nint layer) {
		checkNotNull(layer, nameof(layer));
		return new SurfaceSource(SurfaceSourceKind.MetalLayer, layer, 0, 0);
	}

	/// <summary>
	/// Creates a source for a Wayland surface.
	/// </summary>
	/// <param name="display">The <c>wl_display*</c>.</param>
	/// <param name="surface">The <c>wl_surface*</c>.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="display"/> or <paramref name="surface"/> is null.
	/// </exception>
	public static SurfaceSource DangerousCreateFromWaylandSurface(nint display, nint surface) {
		checkNotNull(display, nameof(display));
		checkNotNull(surface, nameof(surface));
		return new SurfaceSource(SurfaceSourceKind.WaylandSurface, display, surface, 0);
	}

	/// <summary>
	/// Creates a source for an Xlib window.
	/// </summary>
	/// <param name="display">The <c>Display*</c>.</param>
	/// <param name="window">
	/// The <c>Window</c> XID. It's a <see langword="ulong"/> since Xlib's <c>Window</c> is an
	/// <c>unsigned long</c>, even though XIDs only use 32 bits.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="display"/> is null or <paramref name="window"/> is 0
	/// (<c>None</c>).
	/// </exception>
	public static SurfaceSource DangerousCreateFromXlibWindow(nint display, ulong window) {
		checkNotNull(display, nameof(display));
		if (window == 0)
			throw new ArgumentException("Xlib window must not be None", nameof(window));
		return new SurfaceSource(SurfaceSourceKind.XlibWindow, display, 0, window);
	}

	/// <summary>
	/// Creates a source for a Win32 window.
	/// </summary>
	/// <param name="hwnd">The <c>HWND</c>.</param>
	/// <param name="hinstance">The <c>HINSTANCE</c> of the module that created the window.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="hwnd"/> or <paramref name="hinstance"/> is null.
	/// </exception>
	public static SurfaceSource DangerousCreateFromWindowsHwnd(nint hwnd, nint hinstance) {
		checkNotNull(hwnd, nameof(hwnd));
		checkNotNull(hinstance, nameof(hinstance));
		return new SurfaceSource(SurfaceSourceKind.WindowsHwnd, hwnd, hinstance, 0);
	}

	private static void checkNotNull(nint handle, string paramName) {
		if (handle == 0)
			throw new ArgumentException("handle must not be null", paramName);
	}

	// the platform structs live in this stack frame, so the chain never outlives them
	internal unsafe WGPUSurface CreateWgpuSurface(WGPUInstance instance) {
		WGPUSurfaceDescriptor desc = default;
		switch (Kind.Tag) {
		case SurfaceSourceKind.Case.MetalLayer: {
			WGPUSurfaceSourceMetalLayer src = new() {
				chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceMetalLayer },
				layer = (void*)handle0,
			};
			desc.nextInChain = &src.chain;
			return WebgpuException.Check(wgpuInstanceCreateSurface(instance, &desc));
		}
		case SurfaceSourceKind.Case.WaylandSurface: {
			WGPUSurfaceSourceWaylandSurface src = new() {
				chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceWaylandSurface },
				display = (void*)handle0,
				surface = (void*)handle1,
			};
			desc.nextInChain = &src.chain;
			return WebgpuException.Check(wgpuInstanceCreateSurface(instance, &desc));
		}
		case SurfaceSourceKind.Case.XlibWindow: {
			WGPUSurfaceSourceXlibWindow src = new() {
				chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceXlibWindow },
				display = (void*)handle0,
				window = xlibWindow,
			};
			desc.nextInChain = &src.chain;
			return WebgpuException.Check(wgpuInstanceCreateSurface(instance, &desc));
		}
		case SurfaceSourceKind.Case.WindowsHwnd: {
			WGPUSurfaceSourceWindowsHWND src = new() {
				chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceWindowsHWND },
				hwnd = (void*)handle0,
				hinstance = (void*)handle1,
			};
			desc.nextInChain = &src.chain;
			return WebgpuException.Check(wgpuInstanceCreateSurface(instance, &desc));
		}
		default:
			throw new UnreachableException();
		}
	}
}
