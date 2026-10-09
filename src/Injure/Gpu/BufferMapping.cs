// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Gpu;

/// <summary>
/// What a <see cref="GpuBuffer"/> is mapped for.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct MapMode {
	/// <summary>Raw switch tag for <see cref="MapMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Reading GPU-written data on the CPU; requires <see cref="BufferUsage.MapRead"/>.
		/// </summary>
		Read = 1,

		/// <summary>
		/// Writing data on the CPU, for the GPU; requires <see cref="BufferUsage.MapWrite"/>.
		/// </summary>
		Write,
	}
}

/// <summary>
/// The mapping state of a <see cref="GpuBuffer"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Unmapped"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct BufferMapState {
	/// <summary>Raw switch tag for <see cref="BufferMapState"/>.</summary>
	public enum Case {
		/// <summary>
		/// Not mapped; usable by the GPU.
		/// </summary>
		Unmapped,

		/// <summary>
		/// A map was requested with
		/// <see cref="GpuBuffer.BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})"/> and hasn't
		/// completed yet.
		/// </summary>
		Pending,

		/// <summary>
		/// Mapped and accessible from the CPU; not usable by the GPU until unmapped.
		/// </summary>
		Mapped,
	}
}

/// <summary>
/// The outcome of a map request started with
/// <see cref="GpuBuffer.BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct BufferMapStatus {
	/// <summary>Raw switch tag for <see cref="BufferMapStatus"/>.</summary>
	public enum Case {
		/// <summary>
		/// The buffer is now mapped.
		/// </summary>
		Success = 1,

		/// <summary>
		/// The request was cancelled by <see cref="GpuBuffer.Unmap()"/> or
		/// <see cref="GpuBuffer.Dispose()"/> before it completed.
		/// </summary>
		Aborted,

		/// <summary>
		/// WebGPU rejected the request, e.g. because the device was lost.
		/// </summary>
		Error,
	}
}

internal sealed class MapRequest(GpuBuffer buffer, MapMode mode, ulong offset, ulong size, Action<BufferMapStatus>? callback) {
	public readonly GpuBuffer Buffer = buffer;
	public readonly MapMode Mode = mode;
	public readonly ulong Offset = offset;
	public readonly ulong Size = size;
	public readonly Action<BufferMapStatus>? Callback = callback;

	// set by the native callback before the request is queued for dispatch
	public BufferMapStatus Status;
	public string? Message;
}
