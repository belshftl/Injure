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
/// An <see cref="IRenderOutput"/> that presents to a window (or other surface host) through a
/// WebGPU surface.
/// </summary>
/// <remarks>
/// <para>
/// Picks the surface's preferred format and a present mode according to a
/// <see cref="SurfacePresentModePolicy"/>, reconfigures the surface when it becomes outdated or
/// suboptimal, and recreates it when it's lost.
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
	private WGPUTextureFormat format;
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
			return format.FromWebgpuType();
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
	/// <param name="presentPolicy">Policy for picking the present mode.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="device"/> or <paramref name="surfaceHost"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="WebgpuException">
	/// Thrown if the surface can't be created or reports no supported formats or present modes.
	/// </exception>
	public SurfaceRenderOutput(GpuDevice device, ISurfaceHost surfaceHost, SurfacePresentModePolicy presentPolicy) {
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(surfaceHost);
		this.device = device;
		this.surfaceHost = surfaceHost;
		this.presentPolicy = presentPolicy;

		surface = surfaceHost.GetSurfaceSource().CreateWgpuSurface(device.Instance);
		format = getSurfaceFormat();
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

	private WGPUTextureFormat getSurfaceFormat() {
		WGPUSurfaceCapabilities caps;
		wgpuSurfaceGetCapabilities(surface, device.Adapter, &caps);
		try {
			if (caps.formatCount == 0)
				throw new WebgpuException("SurfaceGetCapabilities", "surface doesn't report any supported formats");
			// wgpu says the first format is the most preferred one
			return caps.formats[0];
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

	private WGPUSurfaceConfiguration getSurfaceConfig(uint width, uint height, WGPUPresentMode presentMode) => new() {
		device = device.Device,
		format = format,
		usage = WGPUTextureUsage.RenderAttachment,
		width = width,
		height = height,
		presentMode = presentMode,
		alphaMode = WGPUCompositeAlphaMode.Auto,
	};

	private void reconfigure() {
		(uint w, uint h) = surfaceHost.GetDrawableSize();
		config = getSurfaceConfig(w, h, presentMode);
		fixed (WGPUSurfaceConfiguration* cfg = &config)
			wgpuSurfaceConfigure(surface, cfg);
		Width = w;
		Height = h;
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
			reconfigure();
			wgpuSurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return from(tex.status);
		case WGPUSurfaceGetCurrentTextureStatus.Lost:
			wgpuSurfaceRelease(surface);
			surface = default;
			surface = surfaceHost.GetSurfaceSource().CreateWgpuSurface(device.Instance);
			format = getSurfaceFormat();
			presentMode = getSurfacePresentMode();
			reconfigure();
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
	public void Resized() {
		ObjectDisposedException.ThrowIf(disposed, this);
		reconfigure();
	}

	/// <inheritdoc/>
	/// <exception cref="DeviceLostException">
	/// Thrown if the device was lost.
	/// </exception>
	public bool TryAcquire([NotNullWhen(true)] out IAcquiredOutput? output) {
		ObjectDisposedException.ThrowIf(disposed, this);
		output = null;

		if (needReconfigure)
			reconfigure();

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
			format = format,
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
