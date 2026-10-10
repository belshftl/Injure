// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;
using Injure.Primitives;
using WebGPU;

namespace Injure.Tests.Gpu;

// tests that don't need a GPU
public sealed class GpuValueTests {
	// ==========================================================================
	// SurfaceSource
	[Fact]
	public static void SurfaceSourceFactoriesSetKind() {
		Assert.Equal(SurfaceSourceKind.MetalLayer, SurfaceSource.DangerousCreateFromMetalLayer(1).Kind);
		Assert.Equal(SurfaceSourceKind.WaylandSurface, SurfaceSource.DangerousCreateFromWaylandSurface(1, 2).Kind);
		Assert.Equal(SurfaceSourceKind.XlibWindow, SurfaceSource.DangerousCreateFromXlibWindow(1, 2).Kind);
		Assert.Equal(SurfaceSourceKind.WindowsHwnd, SurfaceSource.DangerousCreateFromWindowsHwnd(1, 2).Kind);
	}

	[Fact]
	public static void SurfaceSourceFactoriesRejectNullHandles() {
		Assert.Equal("layer", Assert.Throws<ArgumentException>(static () => SurfaceSource.DangerousCreateFromMetalLayer(0)).ParamName);
		Assert.Equal("display", Assert.Throws<ArgumentException>(static () => SurfaceSource.DangerousCreateFromWaylandSurface(0, 1)).ParamName);
		Assert.Equal("surface", Assert.Throws<ArgumentException>(static () => SurfaceSource.DangerousCreateFromWaylandSurface(1, 0)).ParamName);
		Assert.Equal("display", Assert.Throws<ArgumentException>(static () => SurfaceSource.DangerousCreateFromXlibWindow(0, 1)).ParamName);
		Assert.Equal("window", Assert.Throws<ArgumentException>(static () => SurfaceSource.DangerousCreateFromXlibWindow(1, 0)).ParamName);
		Assert.Equal("hwnd", Assert.Throws<ArgumentException>(static () => SurfaceSource.DangerousCreateFromWindowsHwnd(0, 1)).ParamName);
		Assert.Equal("hinstance", Assert.Throws<ArgumentException>(static () => SurfaceSource.DangerousCreateFromWindowsHwnd(1, 0)).ParamName);
	}

	// test threads aren't the main thread, so this covers everything that runs before AppKit is touched
	[Fact]
	public static void MacosMetalLayerChecksViewAndThreadFirst() {
		if (!OperatingSystem.IsMacOS())
			Assert.Skip("macOS only");
#pragma warning disable CA1416 // this call site is reachable on all platforms (silenced because it's actually not)
		Assert.Equal("nsView", Assert.Throws<ArgumentException>(static () => MacosMetalLayer.DangerousCreateForView(0)).ParamName);
		// a bogus view is fine here, since the thread check comes before it's used
		Assert.Throws<InvalidOperationException>(static () => MacosMetalLayer.DangerousCreateForView(1));
#pragma warning restore CA1416 // this call site is reachable on all platforms (silenced because it's actually not)
	}

	[Fact]
	public static void DefaultSurfaceSourceCantCreateSurface() =>
		Assert.Throws<InvalidOperationException>(static () => default(SurfaceSource).CreateWgpuSurface(default));

	// ==========================================================================
	// features
	[Fact]
	public static void FeatureTableCoversEveryFeatureOnce() {
		GpuFeatures seen = GpuFeatures.None;
		HashSet<WGPUFeatureName> natives = new();
		foreach ((GpuFeatures feature, WGPUFeatureName native) in GpuFeatureTable.All) {
			Assert.False(seen.HasAny(feature), $"{feature} appears twice");
			Assert.True(natives.Add(native), $"{native} appears twice");
			seen |= feature;
		}
		foreach (GpuFeatures feature in GpuFeatures.Flags.Values)
			Assert.True(seen.HasAll(feature), $"{feature} has no native mapping");
	}

	// ==========================================================================
	// sRGB counterparts
	[Fact]
	public static void SrgbCounterpartsPairEverySrgbFormatWithItsUnormFormat() {
		int srgbCount = 0;
		foreach (TextureFormat.Case tag in Enum.GetValues<TextureFormat.Case>()) {
			TextureFormat f = TextureFormat.Enum.FromTag(tag);
			string name = tag.ToString();
			if (name.EndsWith("UnormSrgb", StringComparison.Ordinal)) {
				srgbCount++;
				TextureFormat plain = f.ToNonSrgb();
				Assert.True(f.IsSrgb(), name);
				Assert.Equal(name[..^"Srgb".Length], plain.Tag.ToString());
				Assert.False(plain.IsSrgb(), name);
				Assert.Equal(f, plain.ToSrgb());
				Assert.Equal(f, f.ToSrgb());
			} else {
				Assert.False(f.IsSrgb(), name);
				Assert.Equal(f, f.ToNonSrgb());
				TextureFormat srgb = f.ToSrgb();
				if (srgb != f)
					Assert.Equal(name + "Srgb", srgb.Tag.ToString());
			}
		}
		Assert.Equal(23, srgbCount);
	}

	// ==========================================================================
	// presets and convenience constructors
	[Fact]
	public static void AttachmentOpsPresets() {
		Assert.Equal(new ColorAttachmentOps(LoadOp.Load, StoreOp.Store, default), ColorAttachmentOps.Load);
		Assert.Equal(new ColorAttachmentOps(LoadOp.Clear, StoreOp.Store, RawColorF128.White), ColorAttachmentOps.Clear(RawColorF128.White));
		Assert.Equal(new DepthAttachmentOps(LoadOp.Clear, StoreOp.Store, 0.25f), DepthAttachmentOps.Clear(0.25f));
		Assert.Equal(LoadOp.Load, DepthAttachmentOps.Load.LoadOp);
		Assert.Equal(new StencilAttachmentOps(LoadOp.Clear, StoreOp.Store, 7), StencilAttachmentOps.Clear(7));
		Assert.Equal(LoadOp.Load, StencilAttachmentOps.Load.LoadOp);
	}

	[Fact]
	public static void BlendStatePresetsUseDocumentedFactors() {
		static void check(BlendState s, BlendFactor colorSrc, BlendFactor colorDst) {
			Assert.Equal(new BlendComponent(BlendOperation.Add, colorSrc, colorDst), s.Color);
			Assert.Equal(new BlendComponent(BlendOperation.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha), s.Alpha);
		}
		check(BlendStates.Alpha, BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);
		check(BlendStates.PremulAlpha, BlendFactor.One, BlendFactor.OneMinusSrcAlpha);
		check(BlendStates.Additive, BlendFactor.SrcAlpha, BlendFactor.One);
		check(BlendStates.PremulAdditive, BlendFactor.One, BlendFactor.One);
	}

	[Fact]
	public static void ConvenienceConstructorsDifferFromDefault() {
		MultisampleState ms = new();
		Assert.Equal(1u, ms.Count);
		Assert.Equal(uint.MaxValue, ms.Mask);
		Assert.Equal(0u, default(MultisampleState).Count);

		GpuTextureViewCreateParams view = new();
		Assert.Equal(TextureAspect.All, view.Aspect);
		Assert.Null(view.Format);
		Assert.Null(view.Dimension);
		Assert.Equal(TextureAspect.Undefined, default(GpuTextureViewCreateParams).Aspect);

		PrimitiveState strip = new(PrimitiveTopology.TriangleStrip);
		Assert.Equal(IndexFormat.Undefined, strip.StripIndexFormat);
		Assert.Equal(FrontFace.Ccw, strip.FrontFace);
		Assert.Equal(CullMode.None, strip.CullMode);
		Assert.False(strip.UnclippedDepth);
	}

	[Fact]
	public static void DeviceOptionsDefaults() {
		GpuDeviceOptions opts = new() { RequiredFeatures = GpuFeatures.None };
		Assert.Equal(PowerPreference.HighPerformance, opts.PowerPreference);
		Assert.Equal(BackendType.Undefined, opts.BackendType);
		Assert.Equal(GpuFeatures.None, opts.OptionalFeatures);
		Assert.Null(opts.CompatibleHost);
		Assert.Equal(PowerPreference.Undefined, default(GpuDeviceOptions).PowerPreference);
	}

	[Fact]
	public static void MapStatusConversion() {
		Assert.Equal(BufferMapStatus.Success, WGPUMapAsyncStatus.Success.FromWebgpuType());
		Assert.Equal(BufferMapStatus.Aborted, WGPUMapAsyncStatus.Aborted.FromWebgpuType());
		Assert.Equal(BufferMapStatus.Error, WGPUMapAsyncStatus.Error.FromWebgpuType());
		Assert.Equal(BufferMapStatus.Error, WGPUMapAsyncStatus.InstanceDropped.FromWebgpuType());
		Assert.Equal(BufferMapStatus.Error, WGPUMapAsyncStatus.Unknown.FromWebgpuType());
	}

	// ==========================================================================
	// RenderFrame argument handling
	private sealed class Recording : IAcquiredOutput {
		public bool Disposed;
		public GpuTextureViewRef View => throw new InvalidOperationException();
		public void Present() => throw new InvalidOperationException();
		public void Dispose() => Disposed = true;
	}

	[Fact]
	public static void RenderFrameDisposesOutputIfConstructionFails() {
		Recording output = new();
		Assert.Throws<ArgumentNullException>(() => new RenderFrame(null!, output));
		Assert.True(output.Disposed);
	}

	[Fact]
	public static void RenderFrameTryBeginRejectsNulls() {
		Assert.Throws<ArgumentNullException>(static () => RenderFrame.TryBegin(null!, null!, out _));
	}
}
