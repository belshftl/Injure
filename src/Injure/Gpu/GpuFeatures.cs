// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Optional WebGPU features, enabled through <see cref="GpuDeviceOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each feature is the WebGPU feature of the same name; see
/// <see href="https://www.w3.org/TR/webgpu/#features"/>. Using functionality that needs a feature
/// the device doesn't have fails WebGPU validation.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </para>
/// </remarks>
[ClosedFlags]
public readonly partial struct GpuFeatures {
	/// <summary>Raw bits for <see cref="GpuFeatures"/>.</summary>
	[Flags]
	public enum Bits : ulong {
		/// <summary>No features.</summary>
		None = 0,

		/// <summary>
		/// <c>depth-clip-control</c>: allows <see cref="PrimitiveState.UnclippedDepth"/>.
		/// </summary>
		DepthClipControl = 1ul << 0,

		/// <summary>
		/// <c>depth32float-stencil8</c>: allows <see cref="TextureFormat.Depth32FloatStencil8"/>.
		/// </summary>
		Depth32FloatStencil8 = 1ul << 1,

		/// <summary>
		/// <c>texture-compression-bc</c>: allows the BC texture formats; common on desktop GPUs.
		/// </summary>
		TextureCompressionBc = 1ul << 2,

		/// <summary>
		/// <c>texture-compression-bc-sliced-3d</c>: allows 3D textures with BC formats.
		/// </summary>
		TextureCompressionBcSliced3d = 1ul << 3,

		/// <summary>
		/// <c>texture-compression-etc2</c>: allows the ETC2 and EAC texture formats; common on
		/// mobile and Apple GPUs.
		/// </summary>
		TextureCompressionEtc2 = 1ul << 4,

		/// <summary>
		/// <c>texture-compression-astc</c>: allows the ASTC texture formats; common on mobile and
		/// Apple GPUs.
		/// </summary>
		TextureCompressionAstc = 1ul << 5,

		/// <summary>
		/// <c>texture-compression-astc-sliced-3d</c>: allows 3D textures with ASTC formats.
		/// </summary>
		TextureCompressionAstcSliced3d = 1ul << 6,

		/// <summary>
		/// <c>indirect-first-instance</c>: allows a nonzero first instance in indirect draws.
		/// </summary>
		IndirectFirstInstance = 1ul << 7,

		/// <summary>
		/// <c>shader-f16</c>: allows the <c>f16</c> type in WGSL.
		/// </summary>
		ShaderF16 = 1ul << 8,

		/// <summary>
		/// <c>rg11b10ufloat-renderable</c>: allows rendering to
		/// <see cref="TextureFormat.Rg11b10Ufloat"/>.
		/// </summary>
		Rg11b10UfloatRenderable = 1ul << 9,

		/// <summary>
		/// <c>bgra8unorm-storage</c>: allows <see cref="TextureFormat.Bgra8Unorm"/> storage textures.
		/// </summary>
		Bgra8UnormStorage = 1ul << 10,

		/// <summary>
		/// <c>float32-filterable</c>: allows filtering the 32-bit float texture formats.
		/// </summary>
		Float32Filterable = 1ul << 11,

		/// <summary>
		/// <c>float32-blendable</c>: allows blending into the 32-bit float texture formats.
		/// </summary>
		Float32Blendable = 1ul << 12,

		/// <summary>
		/// <c>clip-distances</c>: allows <c>@builtin(clip_distances)</c> in WGSL.
		/// </summary>
		ClipDistances = 1ul << 13,

		/// <summary>
		/// <c>dual-source-blending</c>: allows the <c>Src1</c> blend factors.
		/// </summary>
		DualSourceBlending = 1ul << 14,
	}
}

internal static class GpuFeatureTable {
	public static readonly (GpuFeatures Feature, WGPUFeatureName Native)[] All = [
		(GpuFeatures.DepthClipControl, WGPUFeatureName.DepthClipControl),
		(GpuFeatures.Depth32FloatStencil8, WGPUFeatureName.Depth32FloatStencil8),
		(GpuFeatures.TextureCompressionBc, WGPUFeatureName.TextureCompressionBC),
		(GpuFeatures.TextureCompressionBcSliced3d, WGPUFeatureName.TextureCompressionBCSliced3D),
		(GpuFeatures.TextureCompressionEtc2, WGPUFeatureName.TextureCompressionETC2),
		(GpuFeatures.TextureCompressionAstc, WGPUFeatureName.TextureCompressionASTC),
		(GpuFeatures.TextureCompressionAstcSliced3d, WGPUFeatureName.TextureCompressionASTCSliced3D),
		(GpuFeatures.IndirectFirstInstance, WGPUFeatureName.IndirectFirstInstance),
		(GpuFeatures.ShaderF16, WGPUFeatureName.ShaderF16),
		(GpuFeatures.Rg11b10UfloatRenderable, WGPUFeatureName.RG11B10UfloatRenderable),
		(GpuFeatures.Bgra8UnormStorage, WGPUFeatureName.BGRA8UnormStorage),
		(GpuFeatures.Float32Filterable, WGPUFeatureName.Float32Filterable),
		(GpuFeatures.Float32Blendable, WGPUFeatureName.Float32Blendable),
		(GpuFeatures.ClipDistances, WGPUFeatureName.ClipDistances),
		(GpuFeatures.DualSourceBlending, WGPUFeatureName.DualSourceBlending),
	];
}
