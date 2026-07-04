// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

/// <summary>
/// Describes one binding entry in a bind group layout.
/// </summary>
/// <param name="Binding">Binding index within the layout.</param>
/// <param name="Visibility">Shader stages allowed to access the binding.</param>
/// <param name="Layout">Binding layout for the bound resource.</param>
public readonly record struct GpuBindGroupLayoutEntry(
	uint Binding,
	ShaderStage Visibility,
	GpuBindingLayout Layout
);

/// <summary>
/// Base type for binding layout descriptions used by
/// <see cref="WebGpuDevice.CreateBindGroupLayout(ReadOnlySpan{GpuBindGroupLayoutEntry})"/>.
/// </summary>
public abstract record GpuBindingLayout;

/// <summary>
/// Describes a buffer binding layout.
/// </summary>
/// <param name="Type">Buffer binding type.</param>
/// <param name="HasDynamicOffset">Whether the binding uses a dynamic offset.</param>
/// <param name="MinBindingSize">Minimum buffer range size in bytes required by the binding.</param>
public sealed record GpuBufferBindingLayout(
	BufferBindingType Type,
	bool HasDynamicOffset = false,
	ulong MinBindingSize = 0
) : GpuBindingLayout;

/// <summary>
/// Describes a sampler binding layout.
/// </summary>
/// <param name="Type">Sampler binding type.</param>
public sealed record GpuSamplerBindingLayout(
	SamplerBindingType Type
) : GpuBindingLayout;

/// <summary>
/// Describes a storage texture binding layout.
/// </summary>
/// <param name="Access">Storage access mode.</param>
/// <param name="Format">Storage texture format.</param>
/// <param name="ViewDimension">Expected texture view dimension.</param>
public sealed record GpuStorageTextureBindingLayout(
	StorageTextureAccess Access,
	TextureFormat Format,
	TextureViewDimension ViewDimension
) : GpuBindingLayout;

/// <summary>
/// Describes a sampled texture binding layout.
/// </summary>
/// <param name="SampleType">Texture sample type.</param>
/// <param name="ViewDimension">Expected texture view dimension.</param>
/// <param name="Multisampled">Whether the bound view is multisampled.</param>
public sealed record GpuTextureBindingLayout(
	TextureSampleType SampleType,
	TextureViewDimension ViewDimension,
	bool Multisampled = false
) : GpuBindingLayout;
