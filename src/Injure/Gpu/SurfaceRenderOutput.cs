// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Injure.DevAnalyzers.Attributes;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Policy for selecting a surface present mode in <see cref="SurfaceRenderOutput"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct SurfacePresentModePolicy {
	/// <summary>Raw switch tag for <see cref="SurfacePresentModePolicy"/>.</summary>
	public enum Case {
		/// <summary>
		/// Prefer <see cref="PresentMode.Mailbox"/>, fall back to <see cref="PresentMode.Fifo"/>
		/// if not present.
		/// </summary>
		/// <remarks>
		/// Tear-free.
		/// </remarks>
		AutoMailbox = 1,

		/// <summary>
		/// Prefer <see cref="PresentMode.FifoRelaxed"/>, fall back to <see cref="PresentMode.Mailbox"/>
		/// and then <see cref="PresentMode.Fifo"/> if not present.
		/// </summary>
		/// <remarks>
		/// Normally tear-free; may tear if a frame remains on the frontbuffer for more than one vblank.
		/// </remarks>
		AutoFifoRelaxed,

		/// <summary>
		/// Prefer <see cref="PresentMode.Immediate"/>, fall back to <see cref="PresentMode.Mailbox"/>,
		/// then <see cref="PresentMode.FifoRelaxed"/>, and finally <see cref="PresentMode.Fifo"/>
		/// if not present.
		/// </summary>
		/// <remarks>
		/// May tear. Lowest latency.
		/// </remarks>
		AutoImmediate,
	}
}

/// <summary>
/// Policy for selecting a surface format in <see cref="SurfaceRenderOutput"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct SurfaceFormatPolicy {
	/// <summary>Raw switch tag for <see cref="SurfaceFormatPolicy"/>.</summary>
	public enum Case {
		/// <summary>
		/// Prefer a non-sRGB format, so values written to it reach the display unchanged.
		/// </summary>
		/// <remarks>
		/// Use this when rendering sRGB-encoded values, e.g. with <see cref="Draw.Canvas"/>.
		/// </remarks>
		PreferNonSrgb = 1,

		/// <summary>
		/// Prefer an sRGB format, so linear values written to it are encoded for the display.
		/// </summary>
		/// <remarks>
		/// Use this when rendering linear values.
		/// </remarks>
		PreferSrgb,
	}
}

/// <summary>
/// An <see cref="IRenderOutput"/> that presents to a window (or other surface host) through a
/// WebGPU surface.
/// </summary>
/// <remarks>
/// <para>
/// Picks a format according to a <see cref="SurfaceFormatPolicy"/> and a present mode according
/// to a <see cref="SurfacePresentModePolicy"/>, reconfigures the surface when it becomes outdated or
/// suboptimal or the host's drawable size changes, and recreates it when it's lost.
/// </para>
/// <para>
/// Images acquired from it must be presented or disposed before it's disposed; see
/// <see cref="RenderFrame"/>. Not thread-safe.
/// </para>
/// </remarks>
public sealed unsafe class SurfaceRenderOutput : IRenderOutput {
	private enum AcquireStatus {
		Acquired,
		AcquiredNeedsReconfigure,
		SkipFrame,
		DeviceLost,
	}

	private sealed class AcquiredSurfaceTexture(WGPUSurface surface, WGPUTexture texture, GpuTextureView view) : IAcquiredOutput {
		private bool disposed = false;

		public GpuTextureViewRef View {
			get {
				ObjectDisposedException.ThrowIf(disposed, this);
				return field;
			}
		} = view.AsRef();

		public void Present() {
			ObjectDisposedException.ThrowIf(disposed, this);
			wgpuSurfacePresent(surface);
		}

		public void Dispose() {
			if (disposed)
				return;
			disposed = true;
			view.Dispose();
			wgpuTextureRelease(texture);
		}
	}

	private readonly GpuDevice device;
	private readonly ISurfaceHost surfaceHost;
	private SurfacePresentModePolicy presentPolicy;

	private WGPUSurface surface;
	private readonly SurfaceFormatPolicy formatPolicy;
	private WGPUTextureFormat format;
	// differs from format if the surface only offers the other kind of sRGB-ness
	private WGPUTextureFormat viewFormat;
	private WGPUPresentMode presentMode;
	private WGPUSurfaceConfiguration config;
	private bool needReconfigure = false;
	private bool disposed = false;

	/// <inheritdoc/>
	public uint Width {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return field;
		}
		private set;
	}
	/// <inheritdoc/>
	public uint Height {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return field;
		}
		private set;
	}
	/// <inheritdoc/>
	public TextureFormat Format {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return viewFormat.FromWebgpuType();
		}
	}

	/// <summary>
	/// Creates a surface on <paramref name="surfaceHost"/> and configures it for
	/// <paramref name="device"/>.
	/// </summary>
	/// <param name="device">Device to render with.</param>
	/// <param name="surfaceHost">
	/// Host of the surface; must stay alive until this output is disposed.
	/// </param>
	/// <param name="formatPolicy">Policy for picking the format.</param>
	/// <param name="presentPolicy">Policy for picking the present mode.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> or <paramref name="surfaceHost"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="WebgpuException">
	/// Thrown if the surface can't be created or reports no supported formats or present modes.
	/// </exception>
	public SurfaceRenderOutput(GpuDevice device, ISurfaceHost surfaceHost, SurfaceFormatPolicy formatPolicy, SurfacePresentModePolicy presentPolicy) {
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(surfaceHost);
		this.device = device;
		this.surfaceHost = surfaceHost;
		this.formatPolicy = formatPolicy;
		this.presentPolicy = presentPolicy;

		surface = surfaceHost.GetSurfaceSource().CreateWgpuSurface(device.Instance);
		(format, viewFormat) = getSurfaceFormat();
		presentMode = getSurfacePresentMode();
		reconfigure();
	}

	/// <summary>
	/// Changes the present mode policy; takes effect from the next acquired image.
	/// </summary>
	public void SetPresentModePolicy(SurfacePresentModePolicy policy) {
		ObjectDisposedException.ThrowIf(disposed, this);
		presentPolicy = policy;
		presentMode = getSurfacePresentMode();
		needReconfigure = true;
	}

	private (WGPUTextureFormat Format, WGPUTextureFormat ViewFormat) getSurfaceFormat() {
		WGPUSurfaceCapabilities caps;
		wgpuSurfaceGetCapabilities(surface, device.Adapter, &caps);
		try {
			if (caps.formatCount == 0)
				throw new WebgpuException("SurfaceGetCapabilities", "surface doesn't report any supported formats");
			ReadOnlySpan<WGPUTextureFormat> formats = new(caps.formats, (int)caps.formatCount);
			bool wantSrgb = formatPolicy.Tag switch {
				SurfaceFormatPolicy.Case.PreferNonSrgb => false,
				SurfaceFormatPolicy.Case.PreferSrgb => true,
				_ => throw new UnreachableException(),
			};
			// wgpu says the formats are in order of preference, so take the first fitting one, then
			// the first one with a fitting view format, then the most preferred one
			foreach (WGPUTextureFormat f in formats)
				if (TextureFormat.Enum.TryFromMirror(f, out TextureFormat tf) && tf.IsSrgb() == wantSrgb)
					return (f, f);
			foreach (WGPUTextureFormat f in formats) {
				if (!TextureFormat.Enum.TryFromMirror(f, out TextureFormat tf))
					continue;
				TextureFormat other = wantSrgb ? tf.ToSrgb() : tf.ToNonSrgb();
				if (other != tf)
					return (f, other.ToWebgpuType());
			}
			return (formats[0], formats[0]);
		} finally {
			wgpuSurfaceCapabilitiesFreeMembers(caps);
		}
	}

	private WGPUPresentMode getSurfacePresentMode() {
		WGPUSurfaceCapabilities caps;
		wgpuSurfaceGetCapabilities(surface, device.Adapter, &caps);
		try {
			if (caps.presentModeCount == 0)
				throw new WebgpuException("SurfaceGetCapabilities", "surface doesn't report any supported present modes");
			ReadOnlySpan<WGPUPresentMode> modes = new(caps.presentModes, (int)caps.presentModeCount);
			bool haveRelaxed = modes.Contains(WGPUPresentMode.FifoRelaxed);
			bool haveMailbox = modes.Contains(WGPUPresentMode.Mailbox);
			bool haveImmediate = modes.Contains(WGPUPresentMode.Immediate);
			switch (presentPolicy.Tag) {
			case SurfacePresentModePolicy.Case.AutoMailbox:
				return haveMailbox ? WGPUPresentMode.Mailbox : WGPUPresentMode.Fifo;
			case SurfacePresentModePolicy.Case.AutoFifoRelaxed:
				if (haveRelaxed)
					return WGPUPresentMode.FifoRelaxed;
				return haveMailbox ? WGPUPresentMode.Mailbox : WGPUPresentMode.Fifo;
			case SurfacePresentModePolicy.Case.AutoImmediate:
				if (haveImmediate)
					return WGPUPresentMode.Immediate;
				if (haveMailbox)
					return WGPUPresentMode.Mailbox;
				return haveRelaxed ? WGPUPresentMode.FifoRelaxed : WGPUPresentMode.Fifo;
			default:
				throw new UnreachableException();
			}
		} finally {
			wgpuSurfaceCapabilitiesFreeMembers(caps);
		}
	}

	// the caller has to keep *pViewFormat alive across wgpuSurfaceConfigure
	private WGPUSurfaceConfiguration getSurfaceConfig(uint width, uint height, WGPUPresentMode presentMode, WGPUTextureFormat* pViewFormat) => new() {
		device = device.Device,
		format = format,
		viewFormatCount = viewFormat != format ? 1u : 0u,
		viewFormats = viewFormat != format ? pViewFormat : null,
		usage = WGPUTextureUsage.RenderAttachment,
		width = width,
		height = height,
		presentMode = presentMode,
		alphaMode = WGPUCompositeAlphaMode.Auto,
	};

	private bool reconfigure() {
		(uint w, uint h) = surfaceHost.GetDrawableSize();
		return reconfigure(w, h);
	}

	// returns false, leaving the surface as it was, for a drawable size of 0, since WebGPU can't
	// configure a zero-sized surface
	private bool reconfigure(uint w, uint h) {
		if (w == 0 || h == 0) {
			needReconfigure = true;
			return false;
		}
		WGPUTextureFormat vf = viewFormat;
		config = getSurfaceConfig(w, h, presentMode, &vf);
		fixed (WGPUSurfaceConfiguration* cfg = &config)
			wgpuSurfaceConfigure(surface, cfg);
		// don't keep a dangling pointer around
		config.viewFormats = null;
		Width = w;
		Height = h;
		needReconfigure = false;
		return true;
	}

	private AcquireStatus acquire(out WGPUSurfaceTexture outTex) {
		static AcquireStatus from(WGPUSurfaceGetCurrentTextureStatus st) => st switch {
			WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal => AcquireStatus.Acquired,
			WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal => AcquireStatus.AcquiredNeedsReconfigure,
			WGPUSurfaceGetCurrentTextureStatus.Timeout => AcquireStatus.SkipFrame,
			WGPUSurfaceGetCurrentTextureStatus.Outdated => AcquireStatus.SkipFrame,
			WGPUSurfaceGetCurrentTextureStatus.Lost => AcquireStatus.SkipFrame,
			WGPUSurfaceGetCurrentTextureStatus.DeviceLost => AcquireStatus.DeviceLost,
			WGPUSurfaceGetCurrentTextureStatus.OutOfMemory => throw new OutOfMemoryException("WebGPU: wgpuSurfaceGetCurrentTexture: out of memory"),
			_ => throw new WebgpuException("wgpuSurfaceGetCurrentTexture", st.ToString()),
		};

		WGPUSurfaceTexture tex = default;
		wgpuSurfaceGetCurrentTexture(surface, &tex);
		switch (tex.status) {
		case WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal:
			outTex = tex;
			return AcquireStatus.Acquired;
		case WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal:
			outTex = tex;
			return AcquireStatus.AcquiredNeedsReconfigure;
		case WGPUSurfaceGetCurrentTextureStatus.Timeout:
			outTex = default;
			return AcquireStatus.SkipFrame;
		case WGPUSurfaceGetCurrentTextureStatus.Outdated:
			if (!reconfigure()) {
				outTex = default;
				return AcquireStatus.SkipFrame;
			}
			wgpuSurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return from(tex.status);
		case WGPUSurfaceGetCurrentTextureStatus.Lost:
			wgpuSurfaceRelease(surface);
			surface = default;
			surface = surfaceHost.GetSurfaceSource().CreateWgpuSurface(device.Instance);
			(format, viewFormat) = getSurfaceFormat();
			presentMode = getSurfacePresentMode();
			if (!reconfigure()) {
				outTex = default;
				return AcquireStatus.SkipFrame;
			}
			wgpuSurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return from(tex.status);
		case WGPUSurfaceGetCurrentTextureStatus.DeviceLost:
			outTex = default;
			return AcquireStatus.DeviceLost;
		case WGPUSurfaceGetCurrentTextureStatus.OutOfMemory:
			throw new OutOfMemoryException("WebGPU: wgpuSurfaceGetCurrentTexture out of memory");
		default:
			throw new WebgpuException("wgpuSurfaceGetCurrentTexture", tex.status.ToString());
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// Recoverable acquire failures (e.g. a swapchain that's being resized) return
	/// <see langword="false"/>. Fatal failures throw.
	/// </para>
	/// <para>
	/// Reconfigures the surface first if the host's drawable size changed since the last
	/// configuration. Returns <see langword="false"/> if the drawable size is zero (e.g. while the
	/// window is minimized).
	/// </para>
	/// <para>
	/// If the surface was lost, it's recreated through <see cref="ISurfaceHost.GetSurfaceSource()"/>,
	/// and anything that throws propagates from here. That happens e.g. when the host's native
	/// object was destroyed before this output was disposed.
	/// </para>
	/// </remarks>
	/// <exception cref="DeviceLostException">
	/// Thrown if the device was lost.
	/// </exception>
	public bool TryAcquire([NotNullWhen(true)] out IAcquiredOutput? output) {
		ObjectDisposedException.ThrowIf(disposed, this);
		output = null;

		(uint w, uint h) = surfaceHost.GetDrawableSize();
		if ((needReconfigure || w != Width || h != Height) && !reconfigure(w, h))
			return false;

		AcquireStatus st = acquire(out WGPUSurfaceTexture currTex);
		if (!(st is AcquireStatus.Acquired or AcquireStatus.AcquiredNeedsReconfigure)) {
			if (st == AcquireStatus.DeviceLost) {
				device.NotifyLost(
					new DeviceLostInfo(
						DeviceLossInfoKind.Provisional,
						DeviceLossEventReason.SurfaceAcquireDeviceLost,
						"got DeviceLost while trying to acquire a surface texture"
					)
				);
				device.ThrowLostException();
			}
			return false;
		}
		if (st == AcquireStatus.AcquiredNeedsReconfigure)
			needReconfigure = true;

		WGPUTextureViewDescriptor tvdesc = new() {
			format = viewFormat,
			dimension = WGPUTextureViewDimension._2D,
			aspect = WGPUTextureAspect.All,
			baseMipLevel = 0,
			mipLevelCount = 1,
			baseArrayLayer = 0,
			arrayLayerCount = 1,
		};
		WGPUTextureView backbufferView = wgpuTextureCreateView(currTex.texture, &tvdesc);
		if (backbufferView.IsNull) {
			wgpuTextureRelease(currTex.texture);
			throw new WebgpuException("wgpuTextureCreateView", "WebGPU call returned null");
		}
		GpuTextureView v = new(
			backbufferView,
			tvdesc.format.FromWebgpuType(),
			tvdesc.dimension.FromWebgpuType(),
			tvdesc.aspect.FromWebgpuType(),
			config.usage.FromWebgpuType(),
			tvdesc.baseMipLevel,
			tvdesc.mipLevelCount,
			tvdesc.baseArrayLayer,
			tvdesc.arrayLayerCount,
			config.width,
			config.height,
			1,
			1
		);
		output = new AcquiredSurfaceTexture(surface, currTex.texture, v);
		return true;
	}

	/// <summary>
	/// Releases the surface.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		if (surface.IsNotNull)
			wgpuSurfaceRelease(surface);
	}
}
