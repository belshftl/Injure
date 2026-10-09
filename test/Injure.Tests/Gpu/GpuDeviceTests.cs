// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;

namespace Injure.Tests.Gpu;

public sealed class GpuDeviceTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	[Fact]
	public void LimitsAreAtLeastWebgpuDefaults() {
		// https://www.w3.org/TR/webgpu/#limit-default
		GpuLimits l = Device.Limits;
		Assert.True(l.MaxTextureDimension2d >= 8192);
		Assert.True(l.MaxBindGroups >= 4);
		Assert.True(l.MaxColorAttachments >= 8);
		Assert.True(l.MaxUniformBufferBindingSize >= 65536);
		Assert.True(l.MinUniformBufferOffsetAlignment is > 0 and <= 256);
		Assert.True(ulong.IsPow2(l.MinUniformBufferOffsetAlignment));
	}

	[Fact]
	public void SharedDeviceHasNoFeaturesAndIsAlive() {
		GpuDevice dev = Device;
		Assert.Equal(GpuFeatures.None, dev.Features);
		Assert.Equal(DeviceState.Alive, dev.State);
	}

	[Fact]
	public void OptionalFeaturesEnableTheSupportedOnes() {
		_ = Device;
		GpuFeatures all = GpuFeatures.None;
		foreach (GpuFeatures f in GpuFeatures.Flags.Values)
			all |= f;
		GpuFeatures supported;
		using (GpuDevice probe = GpuRig.CreateDevice(optional: all))
			supported = probe.Features;
		Assert.True(all.HasAll(supported));

		if (supported == GpuFeatures.None)
			return;
		GpuFeatures one = GpuFeatures.None;
		foreach (GpuFeatures f in GpuFeatures.Flags.Values) {
			if (supported.HasAll(f)) {
				one = f;
				break;
			}
		}
		using GpuDevice dev = GpuRig.CreateDevice(required: one);
		Assert.Equal(one, dev.Features);
	}

	[Fact]
	public void MissingRequiredFeaturesThrowAndNameThem() {
		_ = Device;
		GpuFeatures all = GpuFeatures.None;
		foreach (GpuFeatures f in GpuFeatures.Flags.Values)
			all |= f;
		GpuFeatures supported;
		using (GpuDevice probe = GpuRig.CreateDevice(optional: all))
			supported = probe.Features;
		if (supported == all)
			Assert.Skip("the adapter supports every feature, so there's no missing one to test with");
		WebgpuException e = Assert.Throws<WebgpuException>(() => GpuRig.CreateDevice(required: all));
		foreach (GpuFeatures f in GpuFeatures.Flags.Values)
			if (!supported.HasAll(f))
				Assert.Contains(f.ToString(), e.Message);
	}

	[Fact]
	public void DisposedDeviceThrowsAndReportsState() {
		_ = Device;
		GpuDevice dev = GpuRig.CreateDevice();
		dev.Dispose();
		dev.Dispose();
		Assert.Equal(DeviceState.Disposed, dev.State);
		Assert.Throws<ObjectDisposedException>(() => dev.CreateBuffer(4, BufferUsage.CopyDst));
		Assert.Throws<ObjectDisposedException>(dev.CreateCommandEncoder);
		Assert.Throws<ObjectDisposedException>(() => dev.Poll());
	}

	private sealed class DefaultHost : ISurfaceHost {
		public SurfaceSource GetSurfaceSource() => default;
		public (uint Width, uint Height) GetDrawableSize() => (1, 1);
	}

	[Fact]
	public void DefaultSurfaceSourceIsRejected() {
		_ = Device;
		Assert.Throws<InvalidOperationException>(static () =>
			new GpuDevice(new GpuDeviceOptions { RequiredFeatures = GpuFeatures.None, CompatibleHost = new DefaultHost() })
		);
	}
}
