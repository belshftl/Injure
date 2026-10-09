// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Injure.Gpu;
using Injure.Primitives;

namespace Injure.Tests.Gpu;

// one headless device shared by every test in the "gpu" collection; tests that need a device of
// their own (lifecycle, features) create one through GpuRig.CreateDevice
public sealed class GpuDeviceFixture : IDisposable {
	public readonly GpuDevice? Device;
	public readonly string? Unavailable;
	public readonly GpuErrorCollector Errors = new();

	public GpuDeviceFixture() {
		try {
			Device = new GpuDevice(new GpuDeviceOptions { RequiredFeatures = GpuFeatures.None, ErrorHandler = Errors.Handle });
		} catch (WebgpuException e) {
			Unavailable = e.Message;
		}
	}

	public void Dispose() => Device?.Dispose();
}

[CollectionDefinition("gpu")]
public sealed class GpuCollection : ICollectionFixture<GpuDeviceFixture>;

// fails any test that leaves WebGPU errors on the shared device behind; tests that trigger errors
// on purpose take them from Errors themselves
[Collection("gpu")]
public abstract class GpuTestBase(GpuDeviceFixture fixture) : IDisposable {
	protected GpuDevice Device => GpuRig.Device(fixture);
	protected GpuErrorCollector Errors => fixture.Errors;

#pragma warning disable CA1816 // change Dispose to call GC.SuppressFinalize
	public void Dispose() => Assert.Empty(fixture.Errors.TakeAll());
#pragma warning restore CA1816 // change Dispose to call GC.SuppressFinalize
}

internal static class GpuRig {
	public static readonly TextureFormat Format = TextureFormat.Rgba8Unorm;

	public static GpuDevice Device(GpuDeviceFixture fixture) {
		if (fixture.Device is null)
			Assert.Skip($"no GPU adapter available: {fixture.Unavailable}");
		return fixture.Device;
	}

	public static GpuDevice CreateDevice(
		GpuFeatures required = default,
		GpuFeatures optional = default,
		GpuErrorHandler? errorHandler = null
	) {
		try {
			return new GpuDevice(new GpuDeviceOptions {
				RequiredFeatures = required, OptionalFeatures = optional, ErrorHandler = errorHandler
			});
		} catch (WebgpuException e) when (required == GpuFeatures.None) {
			Assert.Skip($"no GPU adapter available: {e.Message}");
			throw;
		}
	}

	public static GpuTexture Target(
		GpuDevice dev,
		uint w,
		uint h,
		uint samples = 1,
		TextureFormat? format = null,
		TextureUsage extraUsage = default
	) =>
		dev.CreateTexture(new GpuTextureCreateParams(
			w, h, 1, 1, samples, TextureDimension.Dimension2d, format ?? Format,
			TextureUsage.RenderAttachment | (samples == 1 ? TextureUsage.CopySrc : TextureUsage.None) | extraUsage
		));

	public static GpuBuffer BufferWith<T>(GpuDevice dev, BufferUsage usage, ReadOnlySpan<T> data) where T : unmanaged {
		ulong size = (ulong)(data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
		GpuBuffer buf = dev.CreateBuffer((size + 3) & ~0b11ul, usage | BufferUsage.CopyDst);
		dev.WriteToBuffer(buf, 0, data);
		return buf;
	}

	public static uint RowPitch(uint width, uint bytesPerTexel = 4) => (width * bytesPerTexel + 255) / 256 * 256;

	/// <summary>
	/// Reads a single-sampled 4-byte-per-texel texture back into a tightly-packed array.
	/// </summary>
	public static byte[] Read(GpuDevice dev, GpuTextureHandle tex) {
		uint w = tex.Width, h = tex.Height, pitch = RowPitch(w);
		using GpuBuffer buf = dev.CreateBuffer(pitch * h, BufferUsage.MapRead | BufferUsage.CopyDst);
		using (GpuCommandEncoder enc = dev.CreateCommandEncoder()) {
			enc.CopyTextureToBuffer(tex, Whole(tex), buf, new GpuTextureLayout(0, pitch, h));
			dev.Submit(enc.Finish());
		}
		buf.Map(MapMode.Read);
		byte[] texels = new byte[w * h * 4];
		for (uint y = 0; y < h; y++)
			buf.ReadMapped(y * pitch, texels.AsSpan((int)(y * w * 4), (int)(w * 4)));
		buf.Unmap();
		return texels;
	}

	public static Color32 At(byte[] texels, uint width, uint x, uint y) {
		int i = (int)((y * width + x) * 4);
		return new Color32(texels[i], texels[i + 1], texels[i + 2], texels[i + 3]);
	}

	public static GpuTextureRegion Whole(GpuTextureHandle tex) =>
		new(0, 0, 0, tex.Width, tex.Height, 1, 0, TextureAspect.All);

	public static void Run(GpuDevice dev, Action<GpuCommandEncoder> record) {
		using GpuCommandEncoder enc = dev.CreateCommandEncoder();
		record(enc);
		dev.Submit(enc.Finish());
	}

	/// <summary>
	/// A vertex shader that passes 2D positions through + fragment shaders writing a color from a
	/// uniform at group 0 (dynamic offset), to one or two targets.
	/// </summary>
	public const string Wgsl = """
		struct Tint { color: vec4f }
		@group(0) @binding(0) var<uniform> tint: Tint;
		struct Two { @location(0) a: vec4f, @location(1) b: vec4f }
		@vertex fn vs(@location(0) p: vec3f) -> @builtin(position) vec4f { return vec4f(p, 1.0); }
		@fragment fn fs() -> @location(0) vec4f { return tint.color; }
		@fragment fn fs2() -> Two { var o: Two; o.a = tint.color; o.b = tint.color.bgra; return o; }
		""";

	/// <summary>
	/// A triangle covering the whole viewport, at depth <paramref name="z"/>.
	/// </summary>
	public static float[] Fullscreen(float z = 0f) => [-1f, -1f, z, 3f, -1f, z, -1f, 3f, z];

	public static readonly VertexBufferLayout Vertex3 =
		new(12, VertexStepMode.Vertex, [new VertexAttribute(VertexFormat.Float32x3, 0, 0)]);

	public sealed class Tinted : IDisposable {
		public readonly GpuShaderModule Shader;
		public readonly GpuBindGroupLayout Layout;
		public readonly GpuPipelineLayout PipelineLayout;
		public readonly GpuBuffer Uniforms;
		public readonly GpuBindGroup Group;
		public readonly uint Stride;

		// uniform slot i holds colors[i], at a dynamic offset of i * Stride
		public Tinted(GpuDevice dev, params ReadOnlySpan<Color32> colors) {
			Shader = dev.CreateShaderModuleWgsl(Wgsl);
			Layout = dev.CreateUniformBufferBindGroupLayout(ShaderStage.Fragment, 16, hasDynamicOffset: true);
			PipelineLayout = dev.CreatePipelineLayout([Layout]);
			Stride = Math.Max(dev.Limits.MinUniformBufferOffsetAlignment, 16u);
			Uniforms = dev.CreateBuffer(Stride * (ulong)colors.Length, BufferUsage.Uniform | BufferUsage.CopyDst);
			for (int i = 0; i < colors.Length; i++) {
				Color32 c = colors[i];
				dev.WriteToBuffer(Uniforms, (ulong)i * Stride, new System.Numerics.Vector4(c.R, c.G, c.B, c.A) / 255f);
			}
			Group = dev.CreateUniformBufferBindGroup(Layout, Uniforms, size: 16);
		}

		public GpuRenderPipeline Pipeline(GpuDevice dev, ImmutableArray<ColorTargetState> targets, uint samples = 1,
			DepthStencilState? depthStencil = null, PrimitiveState? primitive = null, string fragment = "fs") =>
			dev.CreateRenderPipeline(new GpuRenderPipelineCreateParams(
				PipelineLayout,
				new VertexState(Shader, "vs", [Vertex3]),
				new FragmentState(Shader, fragment, targets),
				Primitive: primitive,
				DepthStencil: depthStencil,
				Multisample: new MultisampleState(samples)
			));

		public void Dispose() {
			Group.Dispose();
			Uniforms.Dispose();
			PipelineLayout.Dispose();
			Layout.Dispose();
			Shader.Dispose();
		}
	}

	public static ColorTargetState Opaque(TextureFormat? format = null) => new(format ?? Format, null, ColorWriteMask.All);

	/// <summary>
	/// An <see cref="IAcquiredOutput"/> over a texture the test owns, recording what's called on it.
	/// </summary>
	public sealed class FakeOutput(GpuTexture target) : IAcquiredOutput {
		public readonly List<string> Calls = new();
		public Action<GpuCommandEncoderHandle>? OnFinal;
		public GpuTextureViewRef View => target.DefaultView;
		public void RecordFinalCommands(GpuCommandEncoderHandle encoder) {
			Calls.Add("final");
			OnFinal?.Invoke(encoder);
		}
		public void Present() => Calls.Add("present");
		public void Dispose() => Calls.Add("dispose");
	}

	public sealed class FakeRenderOutput(GpuTexture target, bool succeed = true) : IRenderOutput {
		public FakeOutput? Last;
		public uint Width => target.Width;
		public uint Height => target.Height;
		public TextureFormat Format => target.Format;
		public void Resized() {
		}
		public bool TryAcquire([NotNullWhen(true)] out IAcquiredOutput? output) {
			output = succeed ? Last = new FakeOutput(target) : null;
			return succeed;
		}
		public void Dispose() {
		}
	}
}
