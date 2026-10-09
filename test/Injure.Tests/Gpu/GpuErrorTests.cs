// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Tests.Gpu;

public sealed class GpuErrorTests(GpuDeviceFixture fixture) : GpuTestBase(fixture) {
	// MAP_READ can only be combined with COPY_DST, which we don't catch, so WebGPU reports it
	private static void createInvalidBuffer(GpuDevice dev) =>
		dev.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.Vertex).Dispose();

	// draws with a pipeline whose bind group 0 is never set, which only WebGPU checks
	private static void drawWithoutBindGroup(RenderPass pass, GpuRenderPipeline p, GpuBuffer vb) {
		pass.SetPipeline(p);
		pass.SetVertexBuffer(0, vb);
		pass.Draw(3);
	}

	[Fact]
	public void DefaultHandlerIsFailFast() {
		Assert.Same(GpuErrorHandlers.FailFast, new GpuDeviceOptions { RequiredFeatures = GpuFeatures.None }.ErrorHandler);
	}

	[Fact]
	public void ErrorsAreReportedBeforeTheFailingCallReturns() {
		createInvalidBuffer(Device);
		GpuError e = Assert.Single(Errors.TakeAll());
		Assert.Equal(GpuErrorKind.Validation, e.Kind);
		Assert.False(string.IsNullOrWhiteSpace(e.Message));
	}

	[Fact]
	public void ErrorsAreReportedOnTheCallingThreadWithTheirDevice() {
		_ = Device;
		List<(GpuDevice Device, int Thread)> calls = new();
		using GpuDevice dev = GpuRig.CreateDevice(
			errorHandler: (d, in _) =>
				calls.Add((d, Environment.CurrentManagedThreadId))
		);
		int worker = 0;
		Thread t = new(() => {
			worker = Environment.CurrentManagedThreadId;
			createInvalidBuffer(dev);
		});
		t.Start();
		t.Join();
		(GpuDevice reportedDevice, int thread) = Assert.Single(calls);
		Assert.Same(dev, reportedDevice);
		Assert.Equal(worker, thread);
	}

	[Fact]
	public void LogHandlerWritesAndContinues() {
		_ = Device;
		StringWriter log = new();
		using GpuDevice dev = GpuRig.CreateDevice(errorHandler: GpuErrorHandlers.Log(log));
		createInvalidBuffer(dev);
		Assert.Contains("WebGPU Validation error: ", log.ToString());
		using GpuBuffer ok = dev.CreateBuffer(16, BufferUsage.CopyDst);
	}

	[Fact]
	public void RecordingErrorsInvalidateTheCommandBuffer() {
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using GpuRig.Tinted tint = new(Device, Color32.Red);
		using GpuRenderPipeline p = tint.Pipeline(Device, [GpuRig.Opaque()]);
		using GpuBuffer vb = GpuRig.BufferWith(Device, BufferUsage.Vertex, GpuRig.Fullscreen().AsSpan());
		using GpuCommandEncoder enc = Device.CreateCommandEncoder();
		using (RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Load))
			drawWithoutBindGroup(pass, p, vb);
		using GpuCommandBuffer cmd = enc.Finish();
		Assert.Equal(GpuErrorKind.Validation, Assert.Single(Errors.TakeAll()).Kind);
		Assert.False(cmd.IsValid);
		Assert.Throws<InvalidOperationException>(() => Device.Submit(cmd));
		Assert.False(cmd.IsConsumed);
	}

	[Fact]
	public void UnrelatedErrorsDontInvalidateCommandBuffers() {
		using GpuCommandEncoder enc = Device.CreateCommandEncoder();
		createInvalidBuffer(Device);
		using GpuCommandBuffer cmd = enc.Finish();
		Assert.Single(Errors.TakeAll());
		Assert.True(cmd.IsValid);
		Device.Submit(cmd);
	}

	[Fact]
	public void FrameWithInvalidCommandsIsNotPresented() {
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using GpuRig.Tinted tint = new(Device, Color32.Red);
		using GpuRenderPipeline p = tint.Pipeline(Device, [GpuRig.Opaque()]);
		using GpuBuffer vb = GpuRig.BufferWith(Device, BufferUsage.Vertex, GpuRig.Fullscreen().AsSpan());
		GpuRig.FakeOutput output = new(target);
		RenderFrame frame = new(Device, output);
		using (RenderPass pass = frame.Encoder.BeginColorPass(target.DefaultView, ColorAttachmentOps.Clear(Color32.Red)))
			drawWithoutBindGroup(pass, p, vb);
		Assert.Throws<InvalidOperationException>(frame.Submit);
		Assert.Single(Errors.TakeAll());
		Assert.Equal(["final", "dispose"], output.Calls);
		frame.Dispose();
	}

	// ==========================================================================
	// invalid objects
	[Fact]
	public void MappingAnInvalidBufferFails() {
		using GpuBuffer bad = Device.CreateBuffer(16, BufferUsage.MapRead | BufferUsage.Vertex);
		BufferMapStatus? status = null;
		bad.BeginMap(MapMode.Read, callback: s => status = s);
		Device.WaitIdle();
		Assert.Equal(BufferMapStatus.Error, status);
		Assert.Equal(BufferMapState.Unmapped, bad.MapState);

		WebgpuException e = Assert.Throws<WebgpuException>(() => bad.Map(MapMode.Read));
		Assert.Equal("wgpuBufferMapAsync", e.Operation);
		Assert.Equal(BufferMapState.Unmapped, bad.MapState);
		Assert.All(Errors.TakeAll(), static e => Assert.Equal(GpuErrorKind.Validation, e.Kind));
	}

	[Fact]
	public void InvalidShaderModuleFlowsIntoInvalidPipelineAndCommands() {
		using GpuShaderModule bad = Device.CreateShaderModuleWgsl("fn oops( {");
		Assert.Single(Errors.TakeAll());
		using GpuPipelineLayout pl = Device.CreatePipelineLayout([]);
		using GpuRenderPipeline p = Device.CreateRenderPipeline(new GpuRenderPipelineCreateParams(
			pl, new VertexState(bad, "vs"), new FragmentState(bad, "fs", [GpuRig.Opaque()])
		));
		Assert.NotEmpty(Errors.TakeAll());

		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		using GpuCommandBuffer cmd = finish(enc => {
			using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Load);
			pass.SetPipeline(p);
			pass.Draw(3);
		});
		Assert.False(cmd.IsValid);
		Assert.NotEmpty(Errors.TakeAll());
	}

	[Fact]
	public void InvalidBufferInACopyInvalidatesTheCommands() {
		// passes our checks, but exceeds the device's buffer size limit
		using GpuBuffer huge = Device.CreateBuffer(Device.Limits.MaxBufferSize + 4, BufferUsage.CopySrc | BufferUsage.CopyDst);
		Assert.Equal(GpuErrorKind.Validation, Assert.Single(Errors.TakeAll()).Kind);
		using GpuBuffer small = Device.CreateBuffer(16, BufferUsage.CopyDst);
		using GpuCommandBuffer cmd = finish(enc => enc.CopyBufferToBuffer(huge, 0, small, 0, 16));
		Assert.False(cmd.IsValid);
		Assert.NotEmpty(Errors.TakeAll());
	}

	// ==========================================================================
	// commands only WebGPU validates
	private GpuCommandBuffer finish(Action<GpuCommandEncoder> record) {
		using GpuCommandEncoder enc = Device.CreateCommandEncoder();
		record(enc);
		return enc.Finish();
	}

	private void assertInvalidCommands(Action<GpuCommandEncoder> record) {
		using GpuCommandBuffer cmd = finish(record);
		Assert.False(cmd.IsValid);
		Assert.Equal(GpuErrorKind.Validation, Assert.Single(Errors.TakeAll()).Kind);
	}

	[Fact]
	public void DrawWithMissingVertexBufferIsInvalid() {
		using GpuRig.Tinted tint = new(Device, Color32.Red);
		using GpuRenderPipeline p = tint.Pipeline(Device, [GpuRig.Opaque()]);
		using GpuTexture target = GpuRig.Target(Device, 4, 4);
		assertInvalidCommands(enc => {
			using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Load);
			pass.SetPipeline(p);
			pass.SetBindGroup(0, tint.Group, [0]);
			pass.Draw(3);
		});
	}

	[Fact]
	public void PipelineIncompatibleWithThePassIsInvalid() {
		using GpuRig.Tinted tint = new(Device, Color32.Red);
		using GpuRenderPipeline rgba = tint.Pipeline(Device, [GpuRig.Opaque()]);
		using GpuRenderPipeline multisampled = tint.Pipeline(Device, [GpuRig.Opaque()], samples: 4);
		using GpuTexture bgra = GpuRig.Target(Device, 4, 4, format: TextureFormat.Bgra8Unorm);
		using GpuTexture single = GpuRig.Target(Device, 4, 4);
		using GpuBuffer vb = GpuRig.BufferWith(Device, BufferUsage.Vertex, GpuRig.Fullscreen().AsSpan());

		void drawInto(GpuCommandEncoder enc, GpuTexture target, GpuRenderPipeline p) {
			using RenderPass pass = enc.BeginColorPass(target.DefaultView, ColorAttachmentOps.Load);
			pass.SetPipeline(p);
			pass.SetBindGroup(0, tint.Group, [0]);
			pass.SetVertexBuffer(0, vb);
			pass.Draw(3);
		}
		assertInvalidCommands(enc => drawInto(enc, bgra, rgba));
		assertInvalidCommands(enc => drawInto(enc, single, multisampled));
	}

	// ==========================================================================
	// compatibility documented as validated only by WebGPU
	[Fact]
	public void PipelineLayoutNotMatchingTheShaderIsReported() {
		using GpuRig.Tinted tint = new(Device, Color32.Red);
		using GpuPipelineLayout empty = Device.CreatePipelineLayout([]);
		using GpuRenderPipeline p = Device.CreateRenderPipeline(new GpuRenderPipelineCreateParams(
			empty, new VertexState(tint.Shader, "vs", [GpuRig.Vertex3]), new FragmentState(tint.Shader, "fs", [GpuRig.Opaque()])
		));
		Assert.Equal(GpuErrorKind.Validation, Assert.Single(Errors.TakeAll()).Kind);
	}

	[Fact]
	public void BindGroupNotMatchingTheLayoutIsReported() {
		using GpuBindGroupLayout layout = Device.CreateUniformBufferBindGroupLayout(ShaderStage.Fragment);
		using GpuSampler sampler = Device.CreateSampler(SamplerStates.NearestClamp);
		using GpuBindGroup bg = Device.CreateBindGroup(layout, [new GpuBindGroupEntry(0, new GpuSamplerBindingResource(sampler))]);
		Assert.Equal(GpuErrorKind.Validation, Assert.Single(Errors.TakeAll()).Kind);
	}
}
