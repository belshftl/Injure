// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;
using Injure.Primitives;
using WebGPU;

namespace Injure.Tests.Gpu;

// tests that don't need a GPU
public static class GpuValueTests {
	// ==========================================================================
	// SurfaceSource
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
		Assert.Throws<InvalidDefaultException>(static () => default(SurfaceSource).CreateWgpuSurface(default));

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
		// guards against the name check matching nothing, which would skip the sRGB half
		Assert.NotEqual(0, srgbCount);
	}

	// ==========================================================================
	// conversions
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
