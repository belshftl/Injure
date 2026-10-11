// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Injure.Draw.PixelConv;
using Injure.Gpu;

namespace Injure.Draw;

/// <summary>
/// Uniform for one draw range, matching <c>CanvasUniform</c> in the built-in shaders.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid but useless, having a zero projection.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct CanvasUniform : IEquatable<CanvasUniform> {
	public Matrix4x4 Projection;
	public float DistanceRangeTexels;
	public float EdgeValue;
	public float SoftnessPixels;
	public float OutlineWidthPixels;
	public Vector4 OutlineColor;

	public static readonly int Size = Unsafe.SizeOf<CanvasUniform>();

	public readonly bool Equals(CanvasUniform other) =>
		Projection == other.Projection &&
		DistanceRangeTexels == other.DistanceRangeTexels &&
		EdgeValue == other.EdgeValue &&
		SoftnessPixels == other.SoftnessPixels &&
		OutlineWidthPixels == other.OutlineWidthPixels &&
		OutlineColor == other.OutlineColor;
	public override readonly bool Equals(object? obj) => obj is CanvasUniform other && Equals(other);
	public override readonly int GetHashCode() => HashCode.Combine(Projection, DistanceRangeTexels, EdgeValue, SoftnessPixels, OutlineWidthPixels, OutlineColor);
}

/// <summary>
/// Everything <see cref="Canvas"/> needs that only depends on the device: shaders, pipelines,
/// layouts, the white texture that untextured draws use, and the pool of per-canvas arenas.
/// </summary>
/// <remarks>
/// Attached to the device, so there's one per <see cref="GpuDevice"/>; obtained with
/// <see cref="For(GpuDevice)"/>.
/// </remarks>
internal sealed class CanvasDeviceResources : IDisposable {
	/// <summary>
	/// What a cached pipeline depends on.
	/// </summary>
	/// <remarks>
	/// The <see langword="default"/> value is invalid.
	/// </remarks>
	public readonly record struct PipelineKey(
		TextureInterpretation Interpretation,
		BlendState? Blend,
		ColorWriteMask WriteMask,
		TextureFormat ColorTargetFormat
	);

	private readonly GpuDevice device;
	private readonly Lock pipelinesLock = new();
	private readonly Dictionary<TextureInterpretation, GpuShaderModule> shaders = new();
	private readonly Dictionary<PipelineKey, GpuRenderPipeline> pipelines = new();

	public readonly GpuBindGroupLayout UniformLayout;
	public readonly GpuPipelineLayout PipelineLayout;
	/// <summary>
	/// Distance between uniforms in a uniform buffer, as required for dynamic offsets.
	/// </summary>
	public readonly uint UniformStride;
	public readonly Texture2d White;
	public readonly CanvasArenaPool ArenaPool;

	public static CanvasDeviceResources For(GpuDevice device) => device.GetOrAttach(static d => new CanvasDeviceResources(d));

	private CanvasDeviceResources(GpuDevice device) {
		this.device = device;
		UniformLayout = device.CreateUniformBufferBindGroupLayout(
			ShaderStage.Vertex | ShaderStage.Fragment,
			(ulong)CanvasUniform.Size,
			hasDynamicOffset: true
		);
		PipelineLayout = device.CreatePipelineLayout([UniformLayout, DrawDeviceResources.For(device).ColorTexture2dLayout]);
		uint align = device.Limits.MinUniformBufferOffsetAlignment;
		UniformStride = ((uint)CanvasUniform.Size + align - 1) / align * align;
		White = new Texture2d(device, 1, 1);
		White.Upload([0xff, 0xff, 0xff, 0xff], 4, PixelFormat.Rgba32_Unorm);
		ArenaPool = new CanvasArenaPool(this);
	}

	public GpuDevice Device => device;

	public GpuRenderPipeline GetPipeline(in PipelineKey key) {
		lock (pipelinesLock) {
			if (pipelines.TryGetValue(key, out GpuRenderPipeline? existing))
				return existing;
			GpuRenderPipeline created = createPipeline(key);
			pipelines.Add(key, created);
			return created;
		}
	}

	private GpuRenderPipeline createPipeline(in PipelineKey key) {
		if (!shaders.TryGetValue(key.Interpretation, out GpuShaderModule? shader)) {
			shader = device.CreateShaderModuleWgsl(loadShader(key.Interpretation));
			shaders.Add(key.Interpretation, shader);
		}
		return device.CreateRenderPipeline(new GpuRenderPipelineCreateParams(
			Layout: PipelineLayout,
			Vertex: new VertexState(
				ShaderModule: shader,
				EntryPoint: "vs_main",
				Buffers: [
					new VertexBufferLayout(
						ArrayStride: (ulong)Vertex2dTextureColor.Size,
						StepMode: VertexStepMode.Vertex,
						Attributes: [
							new VertexAttribute(Format: VertexFormat.Float32x2, Offset: 0, ShaderLocation: 0),
							new VertexAttribute(Format: VertexFormat.Float32x2, Offset: 8, ShaderLocation: 1),
							new VertexAttribute(Format: VertexFormat.Unorm8x4, Offset: 16, ShaderLocation: 2),
						]
					),
				]
			),
			Fragment: new FragmentState(
				ShaderModule: shader,
				EntryPoint: "fs_main",
				Targets: [new ColorTargetState(Format: key.ColorTargetFormat, Blend: key.Blend, WriteMask: key.WriteMask)]
			),
			Primitive: new PrimitiveState(Topology: PrimitiveTopology.TriangleList, FrontFace: FrontFace.Ccw, CullMode: CullMode.None)
		));
	}

	private static string loadShader(TextureInterpretation interp) {
		string name = interp.Tag switch {
			TextureInterpretation.Case.Color => "shaders/textured2dColor.wgsl",
			TextureInterpretation.Case.Rmask => "shaders/textured2dRmask.wgsl",
			TextureInterpretation.Case.Sdf => "shaders/textured2dSdf.wgsl",
			_ => throw new UnreachableException(),
		};
		using Stream stream = typeof(CanvasDeviceResources).Assembly.GetManifestResourceStream(name) ??
			throw new InternalStateException($"built-in shader '{name}' is missing from the assembly");
		using StreamReader sr = new(stream);
		return sr.ReadToEnd();
	}

	public void Dispose() {
		ArenaPool.Dispose();
		White.Dispose();
		lock (pipelinesLock) {
			foreach (GpuRenderPipeline p in pipelines.Values)
				p.Dispose();
			pipelines.Clear();
			foreach (GpuShaderModule s in shaders.Values)
				s.Dispose();
			shaders.Clear();
		}
		PipelineLayout.Dispose();
		UniformLayout.Dispose();
	}
}
