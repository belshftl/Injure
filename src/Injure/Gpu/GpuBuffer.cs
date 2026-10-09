// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Common base type for GPU buffer wrappers, allowing APIs to accept both owning and non-owning
/// wrappers.
/// </summary>
public abstract class GpuBufferHandle {
	internal abstract WGPUBuffer WgpuBuffer { get; }

	/// <summary>
	/// Returns the underlying <see cref="WGPUBuffer"/>, bypassing ownership/lifetime. Dangles once
	/// freed by <see cref="GpuBuffer.Dispose()"/>.
	/// </summary>
	/// <remarks>
	/// <b>The return type is not a stable API and may change without notice.</b> See
	/// <c>docs/conventions/dangerous-get-create.md</c>.
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
/// <remarks>
/// <para>
/// Buffers with <see cref="BufferUsage.MapRead"/> or <see cref="BufferUsage.MapWrite"/> can be
/// mapped into CPU-accessible memory with
/// <see cref="BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})"/> (asynchronous) or
/// <see cref="Map(MapMode, ulong, ulong?)"/> (blocking). A mapped buffer can't be used by the GPU
/// until <see cref="Unmap()"/> is called.
/// </para>
/// <para>
/// Not thread-safe.
/// </para>
/// </remarks>
public sealed unsafe class GpuBuffer : GpuBufferHandle, IDisposable {
	private readonly GpuDevice device;
	private WGPUBuffer buffer;
	private MapRequest? pendingMap;
	private byte* mappedPtr;

	internal GpuBuffer(GpuDevice device, WGPUBuffer buffer, ulong size, BufferUsage usage, bool mappedAtCreation) {
		this.device = device;
		this.buffer = buffer;
		Size = size;
		Usage = usage;
		if (mappedAtCreation)
			setMapped(MapMode.Write, 0, size);
	}

	internal override WGPUBuffer WgpuBuffer => buffer;
	/// <inheritdoc/>
	public override ulong Size { get; }
	/// <inheritdoc/>
	public override BufferUsage Usage { get; }

	/// <summary>
	/// The current mapping state.
	/// </summary>
	/// <remarks>
	/// A pending map only becomes <see cref="BufferMapState.Mapped"/> once its completion has been
	/// dispatched; see <see cref="BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})"/>.
	/// </remarks>
	public BufferMapState MapState { get; private set; }

	/// <summary>
	/// What the buffer is mapped for.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is not currently mapped, i.e. if <see cref="MapState"/> is not
	/// <see cref="BufferMapState.Mapped"/>.
	/// </exception>
	/// <remarks>
	/// A buffer created with <c>mappedAtCreation</c> is mapped for <see cref="MapMode.Write"/>.
	/// </remarks>
	public MapMode MappedMode {
		get {
			if (MapState != BufferMapState.Mapped)
				throw new InvalidOperationException("buffer is not currently mapped");
			return field;
		}
		private set;
	}

	/// <summary>
	/// Byte offset of the mapped range,
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is not currently mapped, i.e. if <see cref="MapState"/> is not
	/// <see cref="BufferMapState.Mapped"/>.
	/// </exception>
	public ulong MappedOffset {
		get {
			if (MapState != BufferMapState.Mapped)
				throw new InvalidOperationException("buffer is not currently mapped");
			return field;
		}
		private set;
	}

	/// <summary>
	/// Size in bytes of the mapped range.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer is not currently mapped, i.e. if <see cref="MapState"/> is not
	/// <see cref="BufferMapState.Mapped"/>.
	/// </exception>
	public ulong MappedSize {
		get {
			if (MapState != BufferMapState.Mapped)
				throw new InvalidOperationException("buffer is not currently mapped");
			return field;
		}
		private set;
	}

	/// <summary>
	/// Creates a non-owning view of this GPU buffer.
	/// </summary>
	public GpuBufferRef AsRef() => new(this);

	/// <summary>
	/// Starts mapping a range of the buffer for CPU access.
	/// </summary>
	/// <param name="mode">What to map the buffer for.</param>
	/// <param name="offset">Byte offset of the range; must be a multiple of 8.</param>
	/// <param name="size">
	/// Size of the range in bytes; must be a multiple of 4. <see langword="null"/> maps up to the
	/// end of the buffer.
	/// </param>
	/// <param name="callback">Invoked with the outcome once the request completes.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the buffer lacks the usage <paramref name="mode"/> requires,
	/// <paramref name="offset"/> isn't a multiple of 8, the size isn't a multiple of 4, or the range
	/// is out of bounds.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="MapState"/> isn't <see cref="BufferMapState.Unmapped"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the buffer has been disposed.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The map completes once the GPU is done with all previously submitted work that uses the buffer.
	/// Completions are only noticed and dispatched (updating <see cref="MapState"/> and invoking
	/// <paramref name="callback"/>) during <see cref="GpuDevice.Poll()"/>,
	/// <see cref="GpuDevice.WaitIdle()"/>, <see cref="GpuDevice.Submit(GpuCommandBuffer)"/> and
	/// <see cref="Map(MapMode, ulong, ulong?)"/> calls on the owning device, on the thread making that
	/// call. An exception thrown by <paramref name="callback"/> propagates out of that call;
	/// completions not yet dispatched by then are dispatched by the next one.
	/// </para>
	/// <para>
	/// <paramref name="callback"/> is also invoked if the request fails or is cancelled by
	/// <see cref="Unmap()"/> or <see cref="Dispose()"/>.
	/// </para>
	/// </remarks>
	public void BeginMap(MapMode mode, ulong offset = 0, ulong? size = null, Action<BufferMapStatus>? callback = null) =>
		_ = beginMap(mode, offset, size, callback);

	/// <summary>
	/// Maps a range of the buffer for CPU access, blocking until the map completes.
	/// </summary>
	/// <inheritdoc cref="BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})" path="/param"/>
	/// <exception cref="WebgpuException">
	/// Thrown if the map fails.
	/// </exception>
	/// <inheritdoc cref="BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})" path="/exception"/>
	/// <remarks>
	/// Blocks until all work submitted to the device so far has completed, so it's best used
	/// sparingly, e.g. for one-off readbacks or tools; per-frame readback should use
	/// <see cref="BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})"/>. Also dispatches other
	/// pending map completions, see
	/// <see cref="BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})"/>.
	/// </remarks>
	public void Map(MapMode mode, ulong offset = 0, ulong? size = null) {
		MapRequest req = beginMap(mode, offset, size, null);
		while (ReferenceEquals(pendingMap, req))
			device.WaitIdle();
		if (req.Status != BufferMapStatus.Success)
			throw new WebgpuException("wgpuBufferMapAsync", req.Message is { Length: > 0 } m ? $"{req.Status}: {m}" : req.Status.ToString());
	}

	private MapRequest beginMap(MapMode mode, ulong offset, ulong? size, Action<BufferMapStatus>? callback) {
		ObjectDisposedException.ThrowIf(buffer.IsNull, this);
		if (MapState != BufferMapState.Unmapped)
			throw new InvalidOperationException($"buffer must be unmapped to be mapped, but is {MapState}");
		BufferUsage needed = mode == MapMode.Read ? BufferUsage.MapRead : BufferUsage.MapWrite;
		if (Usage.HasNone(needed))
			throw new ArgumentException($"buffer must have {needed} set in its usages to be mapped for {mode}", nameof(mode));
		if (offset % 8 != 0)
			throw new ArgumentException("must be a multiple of 8", nameof(offset));
		if (offset > Size)
			throw new ArgumentException("offset is past the end of the buffer", nameof(offset));
		ulong sz = size ?? Size - offset;
		if (sz % 4 != 0)
			throw new ArgumentException("must be a multiple of 4", nameof(size));
		if (sz > Size - offset)
			throw new ArgumentException("range is out of the buffer's bounds", nameof(size));

		MapRequest req = new(this, mode, offset, sz, callback);
		var h = GCHandle.Alloc(req);
		WGPUBufferMapCallbackInfo info = new() {
			mode = WGPUCallbackMode.AllowProcessEvents,
			callback = &onMapped,
			userdata1 = (void*)GCHandle.ToIntPtr(h),
		};
		pendingMap = req;
		MapState = BufferMapState.Pending;
		WGPUMapMode wmode = mode == MapMode.Read ? WGPUMapMode.Read : WGPUMapMode.Write;
		wgpuBufferMapAsync(buffer, wmode, (nuint)offset, (nuint)sz, info);
		return req;
	}

	[UnmanagedCallersOnly]
	private static void onMapped(WGPUMapAsyncStatus status, WGPUStringView message, void* userdata1, void* userdata2) {
		var h = GCHandle.FromIntPtr((nint)userdata1);
		var req = (MapRequest)h.Target!;
		h.Free();
		req.Status = status.FromWebgpuType();
		req.Message = message.ToString();
		req.Buffer.device.EnqueueMapCompletion(req);
	}

	// called by GpuDevice when dispatching, before the request's callback
	internal void CompleteMap(MapRequest req) {
		if (!ReferenceEquals(pendingMap, req))
			return; // cancelled by Unmap/Dispose, which already reset the state
		pendingMap = null;
		if (req.Status == BufferMapStatus.Success)
			setMapped(req.Mode, req.Offset, req.Size);
		else
			MapState = BufferMapState.Unmapped;
	}

	private void setMapped(MapMode mode, ulong offset, ulong size) {
		mappedPtr = mode == MapMode.Read
			? (byte*)wgpuBufferGetConstMappedRange(buffer, (nuint)offset, (nuint)size)
			: (byte*)wgpuBufferGetMappedRange(buffer, (nuint)offset, (nuint)size);
		if (mappedPtr is null && size != 0)
			throw new WebgpuException("wgpuBufferGetMappedRange", "WebGPU call returned null");
		MapState = BufferMapState.Mapped;
		MappedMode = mode;
		MappedOffset = offset;
		MappedSize = size;
	}

	/// <summary>
	/// Copies data out of the mapped range.
	/// </summary>
	/// <param name="offset">Byte offset into the buffer (not into the mapped range).</param>
	/// <param name="dst">Destination; its whole length is read.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the bytes to read aren't all inside the mapped range.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer isn't mapped.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the buffer has been disposed.
	/// </exception>
	public void ReadMapped<T>(ulong offset, Span<T> dst) where T : unmanaged {
		byte* p = mappedRangeAt(offset, (ulong)dst.Length * (ulong)sizeof(T));
		new ReadOnlySpan<T>(p, dst.Length).CopyTo(dst);
	}

	/// <summary>
	/// Copies data into the mapped range.
	/// </summary>
	/// <param name="offset">Byte offset into the buffer (not into the mapped range).</param>
	/// <param name="src">Data to write.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the bytes to write aren't all inside the mapped range.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer isn't mapped for <see cref="MapMode.Write"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the buffer has been disposed.
	/// </exception>
	public void WriteMapped<T>(ulong offset, ReadOnlySpan<T> src) where T : unmanaged {
		byte* p = mappedRangeAt(offset, (ulong)src.Length * (ulong)sizeof(T));
		if (MappedMode != MapMode.Write)
			throw new InvalidOperationException("buffer is mapped for reading");
		src.CopyTo(new Span<T>(p, src.Length));
	}

	/// <summary>
	/// Returns a pointer to the start of the mapped range, which is <see cref="MappedSize"/> bytes
	/// long, bypassing ownership/lifetime and bounds/mode checks. Dangles once the buffer is unmapped
	/// or disposed.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the buffer isn't mapped.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the buffer has been disposed.
	/// </exception>
	/// <remarks>
	/// The memory must not be written to if the buffer is mapped for <see cref="MapMode.Read"/>.
	/// See <c>docs/conventions/dangerous-get-create.md</c>.
	/// </remarks>
	public void* DangerousGetMappedPointer() => mappedRangeAt(MappedOffset, MappedSize);

	private byte* mappedRangeAt(ulong offset, ulong size) {
		ObjectDisposedException.ThrowIf(buffer.IsNull, this);
		if (MapState != BufferMapState.Mapped)
			throw new InvalidOperationException($"buffer isn't mapped, but {MapState}");
		if (offset < MappedOffset || offset - MappedOffset > MappedSize || size > MappedSize - (offset - MappedOffset))
			throw new ArgumentException("range is outside the mapped range", nameof(offset));
		return mappedPtr + (offset - MappedOffset);
	}

	/// <summary>
	/// Unmaps the buffer, making it usable by the GPU again, or cancels a pending map.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <see cref="MapState"/> is <see cref="BufferMapState.Unmapped"/>.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// Thrown if the buffer has been disposed.
	/// </exception>
	/// <remarks>
	/// Writes made to a buffer mapped for <see cref="MapMode.Write"/> become visible to the GPU.
	/// Cancelling a pending map makes its callback receive <see cref="BufferMapStatus.Aborted"/>.
	/// </remarks>
	public void Unmap() {
		ObjectDisposedException.ThrowIf(buffer.IsNull, this);
		if (MapState == BufferMapState.Unmapped)
			throw new InvalidOperationException("buffer isn't mapped");
		wgpuBufferUnmap(buffer);
		resetMapping();
	}

	private void resetMapping() {
		pendingMap = null;
		mappedPtr = null;
		MapState = BufferMapState.Unmapped;
	}

	/// <summary>
	/// Releases the underlying WebGPU buffer.
	/// </summary>
	/// <remarks>
	/// Cancels a pending map, like <see cref="Unmap()"/>.
	/// </remarks>
	public void Dispose() {
		if (buffer.IsNotNull)
			wgpuBufferRelease(buffer);
		buffer = default;
		resetMapping();
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
	/// <inheritdoc/>
	public override ulong Size => source.Size;
	/// <inheritdoc/>
	public override BufferUsage Usage => source.Usage;
}
