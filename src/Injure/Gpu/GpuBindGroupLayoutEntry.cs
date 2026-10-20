// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// Describes one binding entry in a bind group layout.
/// </summary>
/// <param name="Binding">
/// Binding index, matching <c>@binding</c> in the shader; must be unique within the layout.
/// </param>
/// <param name="Visibility">Shader stages allowed to access the binding.</param>
/// <param name="Layout">What kind of resource the binding holds.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct GpuBindGroupLayoutEntry(
	uint Binding,
	ShaderStage Visibility,
	GpuBindingLayout Layout
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}

/// <summary>
/// Base type for binding layout descriptions used by
/// <see cref="GpuDevice.CreateBindGroupLayout(ReadOnlySpan{GpuBindGroupLayoutEntry})"/>.
/// </summary>
public abstract record GpuBindingLayout {
	private protected GpuBindingLayout() {
	}
}

/// <summary>
/// Describes a buffer binding layout.
/// </summary>
/// <param name="Type">Buffer binding type.</param>
/// <param name="HasDynamicOffset">
/// Whether an offset given when setting the bind group, through the <c>dynamicOffsets</c> of
/// <see cref="RenderPass.SetBindGroup(uint, GpuBindGroupHandle, ReadOnlySpan{uint})"/>, is added
/// to the bound range's offset.
/// </param>
/// <param name="MinBindingSize">
/// Minimum size in bytes of the buffer range bound to this binding; 0 means no minimum.
/// </param>
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
/// <param name="Format">
/// Format of the bound view, matching the format of <c>texture_storage_*</c> in the shader.
/// </param>
/// <param name="ViewDimension">
/// Dimension of the bound view. Must not be a cube dimension.
/// </param>
public sealed record GpuStorageTextureBindingLayout(
	StorageTextureAccess Access,
	TextureFormat Format,
	TextureViewDimension ViewDimension
) : GpuBindingLayout;

/// <summary>
/// Describes a sampled texture binding layout.
/// </summary>
/// <param name="SampleType">
/// Kind of values the shader reads; must be compatible with the bound view's format, see
/// <see cref="TextureSampleType"/>.
/// </param>
/// <param name="ViewDimension">
/// Dimension of the bound view, matching the texture type in the shader.
/// </param>
/// <param name="Multisampled">
/// Whether the bound view is multisampled (<c>texture_multisampled_2d</c> and similar). Requires
/// <see cref="TextureViewDimension.Dimension2d"/> and a <paramref name="SampleType"/> other than
/// <see cref="TextureSampleType.Float"/>.
/// </param>
public sealed record GpuTextureBindingLayout(
	TextureSampleType SampleType,
	TextureViewDimension ViewDimension,
	bool Multisampled = false
) : GpuBindingLayout;
