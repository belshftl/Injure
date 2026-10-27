// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Tests.Gpu;

public sealed class RenderFrameTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	private GpuDevice dev => Device;

	[Fact]
	public void SubmitRecordsFinalCommandsThenPresentsThenDisposes() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		using GpuBuffer copy = dev.CreateBuffer(GpuRig.RowPitch(4) * 4, BufferUsage.MapRead | BufferUsage.CopyDst);
		GpuRig.FakeOutput output = new(target) {
			OnFinal = enc => enc.CopyTextureToBuffer(target, GpuRig.Whole(target), copy, new GpuTextureLayout(0, GpuRig.RowPitch(4), 4))
		};

		RenderFrame frame = new(dev, output);
		Assert.True(frame.HasPrimaryOutput);
		Assert.Equal(4u, frame.PrimaryView.Width);
		frame.BeginPrimaryPass(ColorAttachmentOps.Clear(RawColor32.Red.ToRawF128())).Dispose();
		frame.Submit();
		Assert.Equal(["final", "present", "dispose"], output.Calls);

		// the copy recorded by RecordFinalCommands ran after the clear
		copy.Map(MapMode.Read);
		byte[] px = new byte[4];
		copy.ReadMapped(0, px.AsSpan());
		copy.Unmap();
		Assert.Equal(new byte[] { 255, 0, 0, 255 }, px);

		frame.Dispose();
		Assert.Equal(3, output.Calls.Count);
	}

	[Fact]
	public void DisposeWithoutSubmitDropsTheFrame() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		GpuRig.FakeOutput output = new(target);
		using (RenderFrame frame = new(dev, output))
			frame.BeginPrimaryPass(ColorAttachmentOps.Clear(RawColor32.Red.ToRawF128())).Dispose();
		Assert.Equal(["dispose"], output.Calls);
	}

	[Fact]
	public void FrameIsUnusableAfterSubmit() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		RenderFrame frame = new(dev, new GpuRig.FakeOutput(target));
		GpuCommandEncoderRef enc = frame.Encoder;
		frame.Submit();
		Assert.Throws<InvalidOperationException>(() => frame.Encoder);
		Assert.Throws<InvalidOperationException>(() => frame.PrimaryView);
		Assert.Throws<InvalidOperationException>(frame.Submit);
		Assert.Throws<InvalidOperationException>(() => frame.AddDisp(new GpuRig.FakeOutput(target)));
		// the ref handed out earlier is revoked along with the frame's encoder
		Assert.Throws<InvalidOperationException>(() => enc.PushDebugGroup("x"));
	}

	[Fact]
	public void ActivePassBlocksSubmitAndDispose() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		GpuRig.FakeOutput output = new(target);
		RenderFrame frame = new(dev, output);
		RenderPass pass = frame.BeginPrimaryPass(ColorAttachmentOps.Load);
		Assert.Throws<InvalidOperationException>(frame.Submit);
		Assert.Throws<InvalidOperationException>(frame.Dispose);
		Assert.Empty(output.Calls);
		pass.Dispose();
		frame.Submit();
		Assert.Equal(["final", "present", "dispose"], output.Calls);
	}

	[Fact]
	public void HeadlessFrameRunsCommandsWithoutOutput() {
		using GpuBuffer buf = GpuRig.BufferWith(dev, BufferUsage.CopySrc, new uint[] { 1, 2, 3, 4 }.AsSpan());
		using (RenderFrame frame = new(dev, null)) {
			Assert.False(frame.HasPrimaryOutput);
			Assert.Throws<InvalidOperationException>(() => frame.PrimaryView);
			Assert.Throws<InvalidOperationException>(() => frame.BeginPrimaryPass(ColorAttachmentOps.Load));
			frame.Encoder.ClearBuffer(buf);
			frame.Submit();
		}
		using GpuBuffer rb = dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.CopyDst);
		GpuRig.Run(dev, enc => enc.CopyBufferToBuffer(buf, 0, rb, 0, 16));
		rb.Map(MapMode.Read);
		uint[] vals = new uint[4];
		rb.ReadMapped(0, vals.AsSpan());
		rb.Unmap();
		Assert.Equal(new uint[4], vals);
	}

	[Fact]
	public void OrderedDisposablesAreDisposedAfterSubmitAndOnDiscard() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		GpuRig.FakeOutput a = new(target), b = new(target);
		using (RenderFrame frame = new(dev, null)) {
			Assert.Same(a, frame.AddDisp(a));
			frame.Submit();
		}
		using (RenderFrame frame = new(dev, null))
			frame.AddDisp(b);
		Assert.Equal(["dispose"], a.Calls);
		Assert.Equal(["dispose"], b.Calls);
	}

	[Fact]
	public void OrderedDisposablesAreDisposedInReverseOrder() {
		List<string> log = new();
		using (RenderFrame frame = new(dev, null)) {
			frame.AddDisp(new Logged(log, "a"));
			frame.AddDisp(new Logged(log, "b"));
			frame.Submit();
		}
		using (RenderFrame frame = new(dev, null)) {
			frame.AddDisp(new Logged(log, "c"));
			frame.AddDisp(new Logged(log, "d"));
		}
		Assert.Equal(["b", "a", "d", "c"], log);
	}

	[Fact]
	public void FailedSubmitStillEndsTheFrame() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		GpuRig.FakeOutput output = new(target) { OnFinal = _ => throw new InvalidTimeZoneException("final") };
		List<string> log = new();
		RenderFrame frame = new(dev, output);
		frame.AddDisp(new Logged(log, "a"));
		InvalidTimeZoneException e = Assert.Throws<InvalidTimeZoneException>(frame.Submit);
		Assert.Equal("final", e.Message);
		Assert.Equal(["final", "dispose"], output.Calls);
		Assert.Equal(["a"], log);
		Assert.Throws<InvalidOperationException>(frame.Submit);
		frame.Dispose();
		Assert.Equal(["a"], log);
	}

	[Fact]
	public void EndingFailuresAreAggregatedAndDontStopDisposal() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		GpuRig.FakeOutput output = new(target) { OnFinal = _ => throw new InvalidTimeZoneException("final") };
		List<string> log = new();
		RenderFrame frame = new(dev, output);
		frame.AddDisp(new Logged(log, "a"));
		frame.AddDisp(new Throwing("b"));
		frame.AddDisp(new Logged(log, "c"));
		AggregateException e = Assert.Throws<AggregateException>(frame.Submit);
		Assert.Equal(["final", "b"], e.InnerExceptions.Select(x => x.Message));
		Assert.Equal(["c", "a"], log);
	}

	private sealed class Logged(List<string> log, string name) : IDisposable {
		public void Dispose() => log.Add(name);
	}

	private sealed class Throwing(string name) : IDisposable {
		public void Dispose() => throw new InvalidTimeZoneException(name);
	}

	[Fact]
	public void TryBeginFollowsTheOutput() {
		using GpuTexture target = GpuRig.Target(dev, 4, 4);
		GpuRig.FakeRenderOutput skipping = new(target, succeed: false);
		Assert.False(RenderFrame.TryBegin(dev, skipping, out RenderFrame? none));
		Assert.Null(none);

		GpuRig.FakeRenderOutput output = new(target);
		Assert.True(RenderFrame.TryBegin(dev, output, out RenderFrame? frame));
		using (frame) {
			Assert.True(frame.HasPrimaryOutput);
			frame.Submit();
		}
		Assert.Equal(["final", "present", "dispose"], output.Last!.Calls);
	}
}
