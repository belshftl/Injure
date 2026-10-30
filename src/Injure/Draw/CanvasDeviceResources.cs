// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Injure.Draw.PixelConv;
using Injure.Gpu;
using Injure.Primitives;

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
	public RawColorF128 OutlineColor;

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
/// layouts, samplers, the white texture that untextured draws use, and the pool of per-canvas
/// arenas.
/// </summary>
/// <remarks>
/// Attached to the device, so there's one per <see cref="GpuDevice"/>; obtained with
/// <see cref="For(GpuDevice)"/>.
/// </remarks>
internal sealed class CanvasDeviceResources : IDisposable {
#pragma warning disable CS1573 // parameter has no matching <param> tag
	/// <summary>
	/// What a cached pipeline depends on.
	/// </summary>
	/// <param name="AreaWeighted">
	/// Selects <c>fs_area_weighted</c> rather than <c>fs_main</c> for the fs entrypoint. The SDF
	/// shader doesn't have that, so this must be <see langword="false"/> for
	/// <see cref="TextureInterpretation.Sdf"/>. The sampler itself must be bound separately.
	/// </param>
	/// <remarks>
	/// The <see langword="default"/> value is invalid.
	/// </remarks>
	public readonly record struct PipelineKey(
		TextureInterpretation Interpretation,
		bool AreaWeighted,
		BlendState? Blend,
		ColorWriteMask WriteMask,
		TextureFormat ColorTargetFormat
	) : IHasInvalidDefault {
		bool IHasInvalidDefault.IsDefault => Interpretation == default;
	}
#pragma warning restore CS1573 // parameter has no matching <param> tag

	private readonly GpuDevice device;
	private readonly Lock pipelinesLock = new();
	private readonly Dictionary<TextureInterpretation, GpuShaderModule> shaders = new();
	private readonly Dictionary<PipelineKey, GpuRenderPipeline> pipelines = new();

	public readonly GpuBindGroupLayout UniformLayout;
	public readonly GpuBindGroupLayout SamplerLayout;
	public readonly GpuPipelineLayout PipelineLayout;
	/// <summary>
	/// Distance between uniforms in a uniform buffer, as required for dynamic offsets.
	/// </summary>
	public readonly uint UniformStride;
	public readonly Texture2d White;
	public readonly CanvasArenaPool ArenaPool;
	private readonly GpuSampler[] samplers = new GpuSampler[TexSampling.IndexCount];
	private readonly GpuBindGroup[] samplerBindGroups = new GpuBindGroup[TexSampling.IndexCount];

	public static CanvasDeviceResources For(GpuDevice device) => device.GetOrAttach(static d => new CanvasDeviceResources(d));

	private CanvasDeviceResources(GpuDevice device) {
		this.device = device;
		UniformLayout = device.CreateUniformBufferBindGroupLayout(
			ShaderStage.Vertex | ShaderStage.Fragment,
			(ulong)CanvasUniform.Size,
			hasDynamicOffset: true
		);
		SamplerLayout = device.CreateBindGroupLayout([
			new GpuBindGroupLayoutEntry(
				Binding: 0,
				Visibility: ShaderStage.Fragment,
				Layout: new GpuSamplerBindingLayout(Type: SamplerBindingType.Filtering)
			),
		]);
		PipelineLayout = device.CreatePipelineLayout([UniformLayout, DrawDeviceResources.For(device).ColorTexture2dViewLayout, SamplerLayout]);
		foreach (TexFilter filter in TexFilter.Enum.Values) {
			foreach (TexAddressing addr in TexAddressing.Enum.Values) {
				int i = new TexSampling(filter, addr).Index;
				samplers[i] = device.CreateSampler(samplerParams(filter, addr));
				samplerBindGroups[i] = device.CreateBindGroup(SamplerLayout, [new GpuBindGroupEntry(Binding: 0, new GpuSamplerBindingResource(samplers[i]))]);
			}
		}
		uint align = device.Limits.MinUniformBufferOffsetAlignment;
		UniformStride = ((uint)CanvasUniform.Size + align - 1) / align * align;
		White = new Texture2d(device, 1, 1);
		White.Upload([0xff, 0xff, 0xff, 0xff], 4, PixelFormat.Rgba32_Unorm);
		ArenaPool = new CanvasArenaPool(this);
	}

	public GpuDevice Device => device;

	public GpuBindGroup GetSamplerBindGroup(TexSampling sampling) => samplerBindGroups[sampling.Index];

	private static GpuSamplerParams samplerParams(TexFilter filter, TexAddressing addr) {
		AddressMode address = addr.Tag switch {
			TexAddressing.Case.Clamp => AddressMode.ClampToEdge,
			TexAddressing.Case.Repeat => AddressMode.Repeat,
			TexAddressing.Case.MirrorRepeat => AddressMode.MirrorRepeat,
			_ => throw new UnreachableException(),
		};
		// area weighting is the linear sampler plus a shader-side uv adjustment
		(FilterMode f, MipmapFilterMode mip) = filter.Tag switch {
			TexFilter.Case.Nearest => (FilterMode.Nearest, MipmapFilterMode.Nearest),
			TexFilter.Case.Linear or TexFilter.Case.AreaWeighted => (FilterMode.Linear, MipmapFilterMode.Linear),
			_ => throw new UnreachableException(),
		};
		return new GpuSamplerParams(
			AddressModeU: address,
			AddressModeV: address,
			AddressModeW: address,
			MagFilter: f,
			MinFilter: f,
			MipmapFilter: mip
		);
	}

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
		if (key.AreaWeighted && key.Interpretation == TextureInterpretation.Sdf)
			throw new InternalStateException("area-weighted pipeline requested for sdf");
		if (!shaders.TryGetValue(key.Interpretation, out GpuShaderModule? shader)) {
			shader = device.CreateShaderModuleWgsl(loadShader(key.Interpretation));
			shaders.Add(key.Interpretation, shader);
		}
		return device.CreateRenderPipeline(new GpuRenderPipelineParams(
			Layout: PipelineLayout,
			Vertex: new VertexState(
				ShaderModule: shader,
				EntryPoint: "vs_main",
				Buffers: [
					new VertexBufferLayout(
						ArrayStride: Vertex2dTextureColor.Size,
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
				EntryPoint: key.AreaWeighted ? "fs_area_weighted" : "fs_main",
				Targets: [new ColorTargetState(Format: key.ColorTargetFormat, Blend: key.Blend, WriteMask: key.WriteMask)]
			),
			Primitive: new PrimitiveState(Topology: PrimitiveTopology.TriangleList, FrontFace: FrontFace.Ccw, CullMode: CullMode.None)
		));
	}

	private static string loadShader(TextureInterpretation interp) => BuiltinShaderSource.Load(interp.Tag switch {
		TextureInterpretation.Case.Color => "textured2dColor.wgsl",
		TextureInterpretation.Case.Rmask => "textured2dRmask.wgsl",
		TextureInterpretation.Case.Sdf => "textured2dSdf.wgsl",
		_ => throw new UnreachableException(),
	});

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
		foreach (GpuBindGroup g in samplerBindGroups)
			g.Dispose();
		foreach (GpuSampler s in samplers)
			s.Dispose();
		PipelineLayout.Dispose();
		SamplerLayout.Dispose();
		UniformLayout.Dispose();
	}
}
