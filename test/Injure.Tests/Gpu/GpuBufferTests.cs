// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;

namespace Injure.Tests.Gpu;

public sealed class GpuBufferTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private GpuDevice dev => Device;

	private uint[] readBack(GpuBufferHandle src, int count) {
		using GpuBuffer rb = dev.CreateBuffer((ulong)count * 4, BufferUsage.MapRead | BufferUsage.CopyDst);
		GpuRig.Run(dev, enc => enc.CopyBufferToBuffer(src, 0, rb, 0, (ulong)count * 4));
		rb.Map(MapMode.Read);
		uint[] vals = new uint[count];
		rb.ReadMapped(0, vals.AsSpan());
		rb.Unmap();
		return vals;
	}

	// ==========================================================================
	// creation and queue writes
	[Fact]
	public void CreateBufferReportsSizeAndUsage() {
		using GpuBuffer buf = dev.CreateBuffer(64, BufferUsage.Vertex | BufferUsage.CopyDst);
		Assert.Equal(64ul, buf.Size);
		Assert.Equal(BufferUsage.Vertex | BufferUsage.CopyDst, buf.Usage);
		Assert.Equal(BufferMapState.Unmapped, buf.MapState);
		GpuBufferRef r = buf.AsRef();
		Assert.Equal(buf.Size, r.Size);
		Assert.Equal(buf.Usage, r.Usage);
	}

	[Fact]
	public void QueueWritesLandInTheBuffer() {
		using GpuBuffer buf = dev.CreateBuffer(16, BufferUsage.CopySrc | BufferUsage.CopyDst);
		dev.WriteToBuffer(buf, 0, new uint[] { 1, 2, 3, 4 }.AsSpan());
		dev.WriteToBuffer(buf, 8, 30u);
		Assert.Equal(new uint[] { 1, 2, 30, 4 }, readBack(buf, 4));
	}

	[Fact]
	public void MappedAtCreationRequiresMultipleOf4() =>
		Assert.Throws<ArgumentException>(() => dev.CreateBuffer(6, BufferUsage.CopySrc, mappedAtCreation: true));

	[Fact]
	public void MappedAtCreationIsWritableWithoutMapWrite() {
		using GpuBuffer buf = dev.CreateBuffer(16, BufferUsage.CopySrc, mappedAtCreation: true);
		Assert.Equal(BufferMapState.Mapped, buf.MapState);
		Assert.Equal(MapMode.Write, buf.MappedMode);
		Assert.Equal(0ul, buf.MappedOffset);
		Assert.Equal(16ul, buf.MappedSize);
		buf.WriteMapped(0, new uint[] { 5, 6, 7, 8 }.AsSpan());
		buf.Unmap();
		Assert.Equal(new uint[] { 5, 6, 7, 8 }, readBack(buf, 4));
	}

	// ==========================================================================
	// mapping
	[Fact]
	public void BlockingMapReadsGpuWrittenData() {
		using GpuBuffer src = GpuRig.BufferWith(dev, BufferUsage.CopySrc, new uint[] { 9, 8, 7, 6 }.AsSpan());
		using GpuBuffer rb = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		GpuRig.Run(dev, enc => enc.CopyBufferToBuffer(src, 0, rb, 0, 16));
		rb.Map(MapMode.Read, 8, 8);
		Assert.Equal(BufferMapState.Mapped, rb.MapState);
		Assert.Equal(MapMode.Read, rb.MappedMode);
		Assert.Equal(8ul, rb.MappedOffset);
		Assert.Equal(8ul, rb.MappedSize);
		uint[] vals = new uint[2];
		rb.ReadMapped(8, vals.AsSpan());
		Assert.Equal(new uint[] { 7, 6 }, vals);
		Assert.Throws<ArgumentException>(() => rb.ReadMapped(0, vals.AsSpan()));
		Assert.Throws<ArgumentException>(() => rb.ReadMapped(12, vals.AsSpan()));
		rb.Unmap();
	}

	[Fact]
	public void AsyncMapCompletesThroughPoll() {
		using GpuBuffer rb = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		List<BufferMapStatus> statuses = new();
		rb.BeginMap(MapMode.Read, callback: statuses.Add);
		Assert.Equal(BufferMapState.Pending, rb.MapState);
		for (int i = 0; i < 100_000 && statuses.Count == 0; i++)
			dev.Poll();
		Assert.Equal([BufferMapStatus.Success], statuses);
		Assert.Equal(BufferMapState.Mapped, rb.MapState);
		rb.Unmap();
	}

	[Fact]
	public void WaitIdleDispatchesMapCallbacks() {
		using GpuBuffer rb = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		BufferMapStatus? status = null;
		rb.BeginMap(MapMode.Read, callback: s => status = s);
		dev.WaitIdle();
		Assert.Equal(BufferMapStatus.Success, status);
		rb.Unmap();
	}

	[Fact]
	public void UnmapCancelsPendingMap() {
		using GpuBuffer rb = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		BufferMapStatus? status = null;
		rb.BeginMap(MapMode.Read, callback: s => status = s);
		rb.Unmap();
		Assert.Equal(BufferMapState.Unmapped, rb.MapState);
		dev.WaitIdle();
		Assert.Equal(BufferMapStatus.Aborted, status);
		Assert.Equal(BufferMapState.Unmapped, rb.MapState);

		// the buffer can be mapped again afterwards
		rb.Map(MapMode.Read);
		rb.Unmap();
	}

	[Fact]
	public void CallbackExceptionsPropagateOutOfTheDispatchingCall() {
		using GpuBuffer rb = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		rb.BeginMap(MapMode.Read, callback: static _ => throw new InvalidTimeZoneException());
		Assert.Throws<InvalidTimeZoneException>(dev.WaitIdle);
		Assert.Equal(BufferMapState.Mapped, rb.MapState);
		rb.Unmap();
	}

	[Fact]
	public void BeginMapValidatesArguments() {
		using GpuBuffer rb = dev.CreateBuffer(32, BufferUsage.MapRead | BufferUsage.CopyDst);
		using GpuBuffer plain = dev.CreateBuffer(32, BufferUsage.CopyDst);
		Assert.Throws<ArgumentException>(() => plain.BeginMap(MapMode.Read));
		Assert.Throws<ArgumentException>(() => rb.BeginMap(MapMode.Write));
		Assert.Throws<ArgumentException>(() => rb.BeginMap(MapMode.Read, offset: 4));
		Assert.Throws<ArgumentException>(() => rb.BeginMap(MapMode.Read, size: 6));
		Assert.Throws<ArgumentException>(() => rb.BeginMap(MapMode.Read, offset: 8, size: 32));
		Assert.Throws<ArgumentException>(() => rb.BeginMap(MapMode.Read, offset: 40));
		Assert.Equal(BufferMapState.Unmapped, rb.MapState);

		rb.BeginMap(MapMode.Read);
		Assert.Throws<InvalidOperationException>(() => rb.BeginMap(MapMode.Read));
		rb.Unmap();
	}

	[Fact]
	public void MappedAccessRequiresAMapping() {
		using GpuBuffer rb = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		Assert.Throws<InvalidOperationException>(rb.Unmap);
		Assert.Throws<InvalidOperationException>(() => rb.ReadMapped(0, new byte[4].AsSpan()));
		Assert.Throws<InvalidOperationException>(() => rb.MappedMode);
		Assert.Throws<InvalidOperationException>(() => rb.MappedOffset);
		Assert.Throws<InvalidOperationException>(() => rb.MappedSize);
		unsafe {
			Assert.Throws<InvalidOperationException>(() => { _ = rb.DangerousGetMappedPointer(); });
		}

		rb.Map(MapMode.Read);
		Assert.Throws<InvalidOperationException>(() => rb.WriteMapped(0, new byte[4].AsSpan()));
		rb.Unmap();
	}

	[Fact]
	public unsafe void MappedPointerSeesTheMappedRange() {
		using GpuBuffer src = GpuRig.BufferWith(dev, BufferUsage.CopySrc, new uint[] { 1, 2, 3, 4 }.AsSpan());
		using GpuBuffer rb = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		GpuRig.Run(dev, enc => enc.CopyBufferToBuffer(src, 0, rb, 0, 16));
		rb.Map(MapMode.Read, 8, 8);
		uint* p = (uint*)rb.DangerousGetMappedPointer();
		Assert.Equal(3u, p[0]);
		Assert.Equal(4u, p[1]);
		rb.Unmap();
	}

	[Fact]
	public void DisposedBufferThrows() {
		GpuBuffer buf = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		buf.Dispose();
		buf.Dispose();
		Assert.Throws<ObjectDisposedException>(() => buf.BeginMap(MapMode.Read));
		Assert.Throws<ObjectDisposedException>(buf.Unmap);
	}
}
