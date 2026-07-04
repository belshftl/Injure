// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Common base type for GPU buffer wrappers, allowing APIs to accept both
/// owning and non-owning wrappers.
/// </summary>
public abstract class GpuBufferHandle {
	internal abstract WGPUBuffer WgpuBuffer { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUBuffer"/>, bypassing
	/// ownership/lifetime/revocation contracts.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b>
	/// See <c>Docs/conventions/dangerous-get.md</c> on <c>DangerousGet*</c> methods for more info.
	/// </remarks>
	public WGPUBuffer DangerousGetNative() => WgpuBuffer;

	/// <summary>
	/// Size of the buffer in bytes.
	/// </summary>
	public abstract ulong Size { get; }

	/// <summary>
	/// Allowed usages for this buffer.
	/// </summary>
	public abstract BufferUsage Usage { get; }
}

/// <summary>
/// Owning wrapper around a GPU buffer.
/// </summary>
public sealed class GpuBuffer : GpuBufferHandle, IDisposable {
	private WGPUBuffer buffer;

	internal GpuBuffer(WGPUBuffer buffer, ulong size, BufferUsage usage) {
		this.buffer = buffer;
		Size = size;
		Usage = usage;
	}

	internal override WGPUBuffer WgpuBuffer => buffer;
	public override ulong Size { get; }
	public override BufferUsage Usage { get; }

	/// <summary>
	/// Creates a non-owning view of this GPU buffer.
	/// </summary>
	public GpuBufferRef AsRef() => new(this);

	/// <summary>
	/// Releases the underlying WebGPU buffer.
	/// </summary>
	public void Dispose() {
		if (buffer.IsNotNull)
			wgpuBufferRelease(buffer);
		buffer = default;
	}
}

/// <summary>
/// Non-owning wrapper around a GPU buffer.
/// </summary>
public sealed class GpuBufferRef : GpuBufferHandle {
	private readonly GpuBuffer source;
	internal GpuBufferRef(GpuBuffer source) {
		this.source = source;
	}

	internal override WGPUBuffer WgpuBuffer => source.WgpuBuffer;
	public override ulong Size => source.Size;
	public override BufferUsage Usage => source.Usage;
}
