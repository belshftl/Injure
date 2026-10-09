// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The limits of a <see cref="GpuDevice"/>, see <see cref="GpuDevice.Limits"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each property is the WebGPU limit of the same name; see
/// <see href="https://www.w3.org/TR/webgpu/#limits"/> for what they mean. Compute limits are left
/// out until compute is supported.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and has every limit at 0, which no real device
/// has.
/// </para>
/// </remarks>
public readonly record struct GpuLimits {
	/// <summary>
	/// <c>maxTextureDimension1D</c>.
	/// </summary>
	public uint MaxTextureDimension1d { get; private init; }

	/// <summary>
	/// <c>maxTextureDimension2D</c>.
	/// </summary>
	public uint MaxTextureDimension2d { get; private init; }

	/// <summary>
	/// <c>maxTextureDimension3D</c>.
	/// </summary>
	public uint MaxTextureDimension3d { get; private init; }

	/// <summary>
	/// <c>maxTextureArrayLayers</c>.
	/// </summary>
	public uint MaxTextureArrayLayers { get; private init; }

	/// <summary>
	/// <c>maxBindGroups</c>.
	/// </summary>
	public uint MaxBindGroups { get; private init; }

	/// <summary>
	/// <c>maxBindGroupsPlusVertexBuffers</c>.
	/// </summary>
	public uint MaxBindGroupsPlusVertexBuffers { get; private init; }

	/// <summary>
	/// <c>maxBindingsPerBindGroup</c>.
	/// </summary>
	public uint MaxBindingsPerBindGroup { get; private init; }

	/// <summary>
	/// <c>maxDynamicUniformBuffersPerPipelineLayout</c>.
	/// </summary>
	public uint MaxDynamicUniformBuffersPerPipelineLayout { get; private init; }

	/// <summary>
	/// <c>maxDynamicStorageBuffersPerPipelineLayout</c>.
	/// </summary>
	public uint MaxDynamicStorageBuffersPerPipelineLayout { get; private init; }

	/// <summary>
	/// <c>maxSampledTexturesPerShaderStage</c>.
	/// </summary>
	public uint MaxSampledTexturesPerShaderStage { get; private init; }

	/// <summary>
	/// <c>maxSamplersPerShaderStage</c>.
	/// </summary>
	public uint MaxSamplersPerShaderStage { get; private init; }

	/// <summary>
	/// <c>maxStorageBuffersPerShaderStage</c>.
	/// </summary>
	public uint MaxStorageBuffersPerShaderStage { get; private init; }

	/// <summary>
	/// <c>maxStorageTexturesPerShaderStage</c>.
	/// </summary>
	public uint MaxStorageTexturesPerShaderStage { get; private init; }

	/// <summary>
	/// <c>maxUniformBuffersPerShaderStage</c>.
	/// </summary>
	public uint MaxUniformBuffersPerShaderStage { get; private init; }

	/// <summary>
	/// <c>maxUniformBufferBindingSize</c>.
	/// </summary>
	public ulong MaxUniformBufferBindingSize { get; private init; }

	/// <summary>
	/// <c>maxStorageBufferBindingSize</c>.
	/// </summary>
	public ulong MaxStorageBufferBindingSize { get; private init; }

	/// <summary>
	/// <c>minUniformBufferOffsetAlignment</c>.
	/// </summary>
	public uint MinUniformBufferOffsetAlignment { get; private init; }

	/// <summary>
	/// <c>minStorageBufferOffsetAlignment</c>.
	/// </summary>
	public uint MinStorageBufferOffsetAlignment { get; private init; }

	/// <summary>
	/// <c>maxVertexBuffers</c>.
	/// </summary>
	public uint MaxVertexBuffers { get; private init; }

	/// <summary>
	/// <c>maxBufferSize</c>.
	/// </summary>
	public ulong MaxBufferSize { get; private init; }

	/// <summary>
	/// <c>maxVertexAttributes</c>.
	/// </summary>
	public uint MaxVertexAttributes { get; private init; }

	/// <summary>
	/// <c>maxVertexBufferArrayStride</c>.
	/// </summary>
	public uint MaxVertexBufferArrayStride { get; private init; }

	/// <summary>
	/// <c>maxInterStageShaderVariables</c>.
	/// </summary>
	public uint MaxInterStageShaderVariables { get; private init; }

	/// <summary>
	/// <c>maxColorAttachments</c>.
	/// </summary>
	public uint MaxColorAttachments { get; private init; }

	/// <summary>
	/// <c>maxColorAttachmentBytesPerSample</c>.
	/// </summary>
	public uint MaxColorAttachmentBytesPerSample { get; private init; }

	internal static GpuLimits FromWebgpu(in WGPULimits l) => new() {
		MaxTextureDimension1d = l.maxTextureDimension1D,
		MaxTextureDimension2d = l.maxTextureDimension2D,
		MaxTextureDimension3d = l.maxTextureDimension3D,
		MaxTextureArrayLayers = l.maxTextureArrayLayers,
		MaxBindGroups = l.maxBindGroups,
		MaxBindGroupsPlusVertexBuffers = l.maxBindGroupsPlusVertexBuffers,
		MaxBindingsPerBindGroup = l.maxBindingsPerBindGroup,
		MaxDynamicUniformBuffersPerPipelineLayout = l.maxDynamicUniformBuffersPerPipelineLayout,
		MaxDynamicStorageBuffersPerPipelineLayout = l.maxDynamicStorageBuffersPerPipelineLayout,
		MaxSampledTexturesPerShaderStage = l.maxSampledTexturesPerShaderStage,
		MaxSamplersPerShaderStage = l.maxSamplersPerShaderStage,
		MaxStorageBuffersPerShaderStage = l.maxStorageBuffersPerShaderStage,
		MaxStorageTexturesPerShaderStage = l.maxStorageTexturesPerShaderStage,
		MaxUniformBuffersPerShaderStage = l.maxUniformBuffersPerShaderStage,
		MaxUniformBufferBindingSize = l.maxUniformBufferBindingSize,
		MaxStorageBufferBindingSize = l.maxStorageBufferBindingSize,
		MinUniformBufferOffsetAlignment = l.minUniformBufferOffsetAlignment,
		MinStorageBufferOffsetAlignment = l.minStorageBufferOffsetAlignment,
		MaxVertexBuffers = l.maxVertexBuffers,
		MaxBufferSize = l.maxBufferSize,
		MaxVertexAttributes = l.maxVertexAttributes,
		MaxVertexBufferArrayStride = l.maxVertexBufferArrayStride,
		MaxInterStageShaderVariables = l.maxInterStageShaderVariables,
		MaxColorAttachments = l.maxColorAttachments,
		MaxColorAttachmentBytesPerSample = l.maxColorAttachmentBytesPerSample,
	};
}
