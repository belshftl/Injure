// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

/// <summary>
/// Describes one binding entry in a bind group.
/// </summary>
/// <param name="Binding">Binding index within the bind group layout.</param>
/// <param name="Resource">Resource to bind at <paramref name="Binding"/>.</param>
public readonly record struct GpuBindGroupEntry(
	uint Binding,
	GpuBindingResource Resource
);

/// <summary>
/// Base type for bindable resources used by
/// <see cref="WebGpuDevice.CreateBindGroup(GpuBindGroupLayoutHandle, ReadOnlySpan{GpuBindGroupEntry})"/>.
/// </summary>
public abstract record GpuBindingResource;

/// <summary>
/// Describes a buffer range binding.
/// </summary>
/// <param name="Buffer">Buffer to expose through the binding.</param>
/// <param name="Offset">Byte offset into <paramref name="Buffer"/> where the exposed range begins.</param>
/// <param name="Size">
/// Size in bytes of the exposed range.
/// If <see langword="null"/>, the remaining bytes from <paramref name="Offset"/> to the end of the buffer are exposed.
/// </param>
public sealed record GpuBufferBindingResource(
	GpuBufferHandle Buffer,
	ulong Offset = 0,
	ulong? Size = null
) : GpuBindingResource;

/// <summary>
/// Describes a sampler binding.
/// </summary>
/// <param name="Sampler">Sampler to bind.</param>
public sealed record GpuSamplerBindingResource(
	GpuSamplerHandle Sampler
) : GpuBindingResource;

/// <summary>
/// Describes a texture view binding.
/// </summary>
/// <param name="View">View to bind.</param>
public sealed record GpuTextureViewBindingResource(
	GpuTextureViewHandle View
) : GpuBindingResource;
