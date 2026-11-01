// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Injure.Gpu;

/// <summary>
/// A <c>CAMetalLayer</c> attached to an existing <c>NSView</c>, for presenting to a view owned by
/// another UI framework (e.g. Avalonia's <c>NativeControlHost</c>) through
/// <see cref="SurfaceRenderOutput"/>.
/// </summary>
/// <remarks>
/// <para>
/// The layer's color space is set to sRGB, so macOS treats the stored values as sRGB-encoded and
/// converts them for the display. That holds for both <see cref="SurfaceFormatPolicy"/> values,
/// since an sRGB format stores encoded values too.
/// </para>
/// <para>
/// The layer doesn't follow the view's size or scale on its own. The <see cref="ISurfaceHost"/>
/// that uses this layer has to return the view's size in physical pixels from
/// <see cref="ISurfaceHost.GetDrawableSize()"/>, and the host's owner has to keep
/// <see cref="ContentsScale"/> in sync with the view's backing scale factor; otherwise, the
/// presented image will be scaled.
/// </para>
/// <para>
/// Creation and <see cref="ContentsScale"/> calls have to happen on the main thread, as AppKit
/// requires. Not thread-safe.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed partial class MacosMetalLayer : IDisposable {
	private static unsafe partial class Native {
		private const string objc = "/usr/lib/libobjc.A.dylib";
		private const string coreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

		[LibraryImport("/usr/lib/libSystem.B.dylib")]
		public static partial int pthread_main_np();

		[LibraryImport(objc, StringMarshalling = StringMarshalling.Utf8)]
		public static partial nint objc_getClass(string name);

		[LibraryImport(objc, StringMarshalling = StringMarshalling.Utf8)]
		public static partial nint sel_registerName(string name);

		[LibraryImport(objc)]
		public static partial nint objc_msgSend(nint receiver, nint selector);

		[LibraryImport(objc)]
		public static partial void objc_msgSend(nint receiver, nint selector, nint arg);

		[LibraryImport(objc)]
		public static partial void objc_msgSend(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool arg);

		[LibraryImport(objc)]
		public static partial void objc_msgSend(nint receiver, nint selector, double arg);

		[LibraryImport(objc, EntryPoint = "objc_msgSend")]
		public static partial double objc_msgSend__double(nint receiver, nint selector);

		[LibraryImport(objc)]
		public static partial void objc_release(nint obj);

		[LibraryImport(coreGraphics)]
		public static partial nint CGColorSpaceCreateWithName(nint name);

		[LibraryImport(coreGraphics)]
		public static partial void CGColorSpaceRelease(nint space);

		// kCGColorSpaceSRGB is an exported CFStringRef variable, not a function
		public static nint SrgbColorSpaceName() {
			nint lib = NativeLibrary.Load(coreGraphics);
			return *(nint*)NativeLibrary.GetExport(lib, "kCGColorSpaceSRGB");
		}

		public static nint Sel(string name) => sel_registerName(name);
	}

	// one reference of our own, so the layer outlives the view if it has to
	private nint layer;

	/// <summary>
	/// The layer's <c>contentsScale</c>, i.e. physical pixels per point.
	/// </summary>
	/// <remarks>
	/// Starts as the backing scale factor of the view's window, or 1 if the view isn't in a window
	/// yet. Set this whenever the view's backing scale factor changes, e.g. when the window moves to
	/// a display with a different scale.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if attempted to set to a value that isn't finite and positive.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if not called on the main thread.
	/// </exception>
	public double ContentsScale {
		get {
			chk();
			return Native.objc_msgSend__double(layer, Native.Sel("contentsScale"));
		}
		set {
			chk();
			if (!double.IsFinite(value) || value <= 0)
				throw new ArgumentOutOfRangeException(nameof(value), value, "contents scale must be finite and positive");
			Native.objc_msgSend(layer, Native.Sel("setContentsScale:"), value);
		}
	}

	private MacosMetalLayer(nint layer) {
		this.layer = layer;
	}

	/// <summary>
	/// Creates a <c>CAMetalLayer</c> and makes it the backing layer of <paramref name="nsView"/>,
	/// trusting that <paramref name="nsView"/> is a live <c>NSView</c>.
	/// </summary>
	/// <param name="nsView">
	/// The <c>NSView*</c>. Its existing layer, if any, is replaced. The view only has to be alive
	/// during this call; the layer keeps working after the view is destroyed, though nothing is
	/// shown then.
	/// </param>
	/// <remarks>
	/// See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="nsView"/> is null.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if not called on the main thread, or if the layer can't be created.
	/// </exception>
	public static MacosMetalLayer DangerousCreateForView(nint nsView) {
		if (nsView == 0)
			throw new ArgumentException("view must not be null", nameof(nsView));
		chkMainThread();

		nint layer = Native.objc_msgSend(Native.objc_msgSend(Native.objc_getClass("CAMetalLayer"), Native.Sel("alloc")), Native.Sel("init"));
		if (layer == 0)
			throw new InvalidOperationException("couldn't create a CAMetalLayer");
		SetSrgbColorSpace(layer);

		// messages to nil return 0, so a view without a window gets the 1.0 fallback
		double scale = Native.objc_msgSend__double(Native.objc_msgSend(nsView, Native.Sel("window")), Native.Sel("backingScaleFactor"));
		Native.objc_msgSend(layer, Native.Sel("setContentsScale:"), scale > 0 ? scale : 1.0);

		// layer first, then wantsLayer, which makes the view layer-hosting rather than layer-backed
		Native.objc_msgSend(nsView, Native.Sel("setLayer:"), layer);
		Native.objc_msgSend(nsView, Native.Sel("setWantsLayer:"), true);
		return new MacosMetalLayer(layer);
	}

	/// <remarks>
	/// <para>
	/// Apple's documentation states that setting <c>colorspace</c> to <c>nil</c> makes the rendered
	/// content not color-matched, so <c>kCGColorSpaceSRGB</c> must be set explicitly. See
	/// <see href="https://developer.apple.com/documentation/quartzcore/cametallayer/colorspace"/>,
	/// and as a supplementary,
	/// <see href="https://developer.apple.com/forums/thread/724223?answerId=744185022#744185022"/>.
	/// </para>
	/// <para>
	/// Or at least that is in theory. I did some testing by setting <c>colorspace</c> on the Metal
	/// layer obtained through <see cref="Sdl.SdlWindow.DangerousGetMetalLayer()"/>, on a Mac with a
	/// Display P3 display, and <c>nil</c> behaved the exact same as <c>kCGColorSpaceSRGB</c>; drawing
	/// pure red displayed <c>#EA3323</c>, which is exactly sRGB <c>#FF0000</c> converted to Display
	/// P3. Explicitly setting <c>kCGColorSpaceDisplayP3</c>, though, displayed <c>#FF0000</c>, and
	/// was visibly more saturated than the <c>nil</c>/<c>kCGColorSpaceSRGB</c> one. So I don't really
	/// know what's going on. My best guess is that "not color-matched" applies to the layer only, and
	/// <c>NSWindow.colorSpace</c> wins if the layer's <c>colorspace</c> is <c>nil</c>, and that its
	/// default value is sRGB (or is set to sRGB by SDL3); there is no documented default value for
	/// <c>NSWindow.colorSpace</c>, only a note that it's <c>nil</c> if the window doesn't have a
	/// backing store and is off-screen.
	/// </para>
	/// <para>
	/// Either way, that's just observed behavior, and it costs basically nothing to explicitly set it
	/// to <c>kCGColorSpaceSRGB</c>, which <see cref="Sdl.SdlWindow"/> does.
	/// </para>
	/// </remarks>
	internal static void SetSrgbColorSpace(nint layer) {
		nint srgb = Native.CGColorSpaceCreateWithName(Native.SrgbColorSpaceName());
		Native.objc_msgSend(layer, Native.Sel("setColorspace:"), srgb);
		Native.CGColorSpaceRelease(srgb);
	}

	/// <summary>
	/// Gets the source to create a WebGPU surface on, for use in
	/// <see cref="ISurfaceHost.GetSurfaceSource()"/>.
	/// </summary>
	/// <remarks>
	/// Surfaces created from it have to be disposed before this layer is.
	/// </remarks>
	public SurfaceSource GetSurfaceSource() {
		chkDisposed();
		return SurfaceSource.DangerousCreateFromMetalLayer(layer);
	}

	/// <summary>
	/// Returns the <c>CAMetalLayer*</c>, bypassing ownership/lifetime. Dangles once this layer is
	/// disposed and the view has let go of it.
	/// </summary>
	/// <remarks>
	/// Must not be released, and its <c>colorspace</c> must not be changed; other properties (e.g.
	/// <c>opaque</c>) are up to the caller. See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public nint DangerousGetNative() {
		chkDisposed();
		return layer;
	}

	/// <summary>
	/// Releases this object's reference to the layer.
	/// </summary>
	/// <remarks>
	/// The view keeps its own reference until it's destroyed or gets another layer.
	/// </remarks>
	public void Dispose() {
		if (layer == 0)
			return;
		Native.objc_release(layer);
		layer = 0;
	}

	private void chkDisposed() => ObjectDisposedException.ThrowIf(layer == 0, this);

	private void chk() {
		chkDisposed();
		chkMainThread();
	}

	private static void chkMainThread() {
		if (Native.pthread_main_np() == 0)
			throw new InvalidOperationException("AppKit objects can only be used from the main thread");
	}
}
