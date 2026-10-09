// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using WebGPU;
using static WebGPU.WebGPU;
using static Injure.Gpu.WebgpuException;

namespace Injure.Gpu;

/// <summary>
/// A WebGPU device (with its instance, adapter, and queue): creates GPU resources, writes to them,
/// and runs submitted commands.
/// </summary>
/// <remarks>
/// <para>
/// Objects created from a device are only usable while it's alive and not lost. Once it's disposed,
/// its methods throw <see cref="ObjectDisposedException"/>; once it's lost, they throw
/// <see cref="DeviceLostException"/>. These exceptions aren't repeated on each method.
/// </para>
/// <para>
/// Errors WebGPU reports, e.g. for a call that breaks its validation rules, are passed to
/// <see cref="GpuDeviceOptions.ErrorHandler"/> synchronously, on the thread making the failing
/// call, before that call returns. Errors in recorded commands are reported by
/// <see cref="GpuCommandEncoder.Finish()"/>. WebGPU keeps working after an error, but the object or
/// commands involved are invalid, which later calls using them report as further errors. Device
/// loss is not an error in this sense; see <see cref="State"/>.
/// </para>
/// <para>
/// Thread-safety isn't promised yet; use a device and the objects created from it from one thread.
/// </para>
/// </remarks>
public sealed unsafe class GpuDevice : IDisposable {
	// filled by native map callbacks, which may run inside any wgpu call that processes events
	private readonly ConcurrentQueue<MapRequest> mapCompletions = new();

	// ==========================================================================
	// private types
	private sealed class Request<TStatus, TObject> where TStatus : unmanaged, Enum where TObject : unmanaged {
		public TStatus Status;
		public TObject Object;
		public string? Message;
		public readonly ManualResetEventSlim Done = new(false);
	}

	// target of the GCHandle passed as userdata to WebGPU's device callbacks
	private sealed class CallbackState {
		public required GpuDevice Owner;
	}

	// ==========================================================================
	// private constants
	private const uint spirvMagic = 0x07230203;
	private const int spirvHeaderWords = 5;

	// ==========================================================================
	// private state
	internal readonly WGPUInstance Instance;
	internal readonly WGPUAdapter Adapter;
	internal readonly WGPUDevice Device;
	internal readonly WGPUQueue Queue;

	private readonly GCHandle callbackStateHandle;
	private readonly GpuErrorHandler errorHandler;

	// counts errors reported on this thread, across all devices; WebGPU reports errors synchronously
	// on the thread making the failing call, so comparing it before and after a call can determine if
	// that call failed; see GpuCommandEncoder.Finish
	[ThreadStatic] private static ulong errorsOnThread;
	internal static ulong ErrorsOnCurrentThread => errorsOnThread;

	// see GetOrAttach
	private readonly Lock attachmentsLock = new();
	private readonly Dictionary<Type, IDisposable> attachments = new();

	private int disposed = 0;
	private int lost = 0;
	private DeviceLostInfo? lostInfo = null;

	// ==========================================================================
	// public properties and ctor

	/// <summary>
	/// The limits of this device, e.g. the maximum texture size or the alignment required for
	/// dynamic buffer offsets.
	/// </summary>
	public GpuLimits Limits { get; }

	/// <summary>
	/// The features enabled on this device: all of <see cref="GpuDeviceOptions.RequiredFeatures"/>
	/// plus the supported ones of <see cref="GpuDeviceOptions.OptionalFeatures"/>.
	/// </summary>
	public GpuFeatures Features { get; }

	/// <summary>
	/// Whether this device is usable, lost, or disposed.
	/// </summary>
	/// <remarks>
	/// Never throws, unlike the device's other members. Can change from
	/// <see cref="DeviceState.Alive"/> to <see cref="DeviceState.Lost"/> at any time, from any
	/// thread, since WebGPU may report a loss spontaneously.
	/// </remarks>
	public DeviceState State {
		get {
			if (Volatile.Read(ref disposed) != 0)
				return DeviceState.Disposed;
			return Volatile.Read(ref lost) != 0 ? DeviceState.Lost : DeviceState.Alive;
		}
	}

	/// <summary>
	/// Creates a <see cref="GpuDevice"/>.
	/// </summary>
	/// <param name="options">Device configuration.</param>
	/// <exception cref="WebgpuException">
	/// Thrown if no suitable adapter is found, the adapter lacks one of
	/// <see cref="GpuDeviceOptions.RequiredFeatures"/>, or the device can't be created.
	/// </exception>
	public GpuDevice(in GpuDeviceOptions options) {
		WGPUInstanceDescriptor instDesc = default;
		Instance = Check(wgpuCreateInstance(&instDesc));
		WGPUSurface compatibleSurface = options.CompatibleHost?.GetSurfaceSource().CreateWgpuSurface(Instance) ?? default;
		try {
			Adapter = requestAdapterBlocking(
				Instance,
				compatibleSurface,
				options.PowerPreference.ToWebgpuType(),
				options.BackendType.ToWebgpuType()
			);
		} finally {
			if (compatibleSurface.IsNotNull)
				wgpuSurfaceRelease(compatibleSurface);
		}
		Features = selectFeatures(Adapter, options.RequiredFeatures, options.OptionalFeatures);
		errorHandler = options.ErrorHandler ?? GpuErrorHandlers.FailFast;
		Device = requestDeviceBlocking(this, Adapter, Features, out callbackStateHandle);
		Queue = Check(wgpuDeviceGetQueue(Device));
		WGPULimits limits = default;
		if (wgpuDeviceGetLimits(Device, &limits) != WGPUStatus.Success)
			throw new WebgpuException("wgpuDeviceGetLimits", "call failed");
		Limits = GpuLimits.FromWebgpu(limits);
	}

	// ==========================================================================
	// resource acquisition
	private static WGPUAdapter requestAdapterBlocking(
		WGPUInstance instance,
		WGPUSurface compatibleSurface,
		WGPUPowerPreference powerPreference,
		WGPUBackendType backendType
	) {
		Request<WGPURequestAdapterStatus, WGPUAdapter> req = new();
		var h = GCHandle.Alloc(req);
		try {
			WGPURequestAdapterOptions opts = new() {
				compatibleSurface = compatibleSurface,
				powerPreference = powerPreference,
				backendType = backendType,
			};
			WGPURequestAdapterCallbackInfo cb = new() {
				mode = WGPUCallbackMode.AllowSpontaneous,
				callback = &adapterRequestCallback,
				userdata1 = (void*)GCHandle.ToIntPtr(h),
			};
			WGPUFuture future = wgpuInstanceRequestAdapter(instance, &opts, cb);
			req.Done.Wait();
			if (req.Status != WGPURequestAdapterStatus.Success || req.Object.IsNull)
				throw new WebgpuException("wgpuInstanceRequestAdapter", req.Message ?? req.Status.ToString());
			return req.Object;
		} finally {
			h.Free();
		}
	}

	[UnmanagedCallersOnly]
	private static void adapterRequestCallback(
		WGPURequestAdapterStatus status,
		WGPUAdapter adapter,
		WGPUStringView message,
		void* userdata1,
		void* userdata2
	) {
		var h = GCHandle.FromIntPtr((nint)userdata1);
		var req = (Request<WGPURequestAdapterStatus, WGPUAdapter>)h.Target!;
		req.Status = status;
		req.Object = adapter;
		req.Message = message.ToString();
		req.Done.Set();
	}

	private static GpuFeatures selectFeatures(WGPUAdapter adapter, GpuFeatures required, GpuFeatures optional) {
		GpuFeatures selected = GpuFeatures.None;
		List<string>? missing = null;
		foreach ((GpuFeatures feature, WGPUFeatureName native) in GpuFeatureTable.All) {
			bool supported = wgpuAdapterHasFeature(adapter, native);
			if (required.HasAll(feature)) {
				if (supported)
					selected |= feature;
				else
					(missing ??= new List<string>()).Add(feature.ToString());
			} else if (optional.HasAll(feature) && supported) {
				selected |= feature;
			}
		}
		if (missing is not null)
			throw new WebgpuException("wgpuAdapterHasFeature", $"adapter lacks required features: {string.Join(", ", missing)}");
		return selected;
	}

	private static WGPUDevice requestDeviceBlocking(GpuDevice owner, WGPUAdapter adapter, GpuFeatures features, out GCHandle callbackStateHandle) {
		Request<WGPURequestDeviceStatus, WGPUDevice> req = new();
		CallbackState st = new() { Owner = owner };
		var reqHandle = GCHandle.Alloc(req);
		var stHandle = GCHandle.Alloc(st);
		try {
			WGPUFeatureName* featureNames = stackalloc WGPUFeatureName[GpuFeatureTable.All.Length];
			int featureCount = 0;
			foreach ((GpuFeatures feature, WGPUFeatureName native) in GpuFeatureTable.All)
				if (features.HasAll(feature))
					featureNames[featureCount++] = native;
			WGPUDeviceDescriptor desc = new() {
				requiredFeatureCount = (nuint)featureCount,
				requiredFeatures = featureNames,
				deviceLostCallbackInfo = new WGPUDeviceLostCallbackInfo {
					mode = WGPUCallbackMode.AllowSpontaneous,
					callback = &deviceLostCallback,
					userdata1 = (void*)GCHandle.ToIntPtr(stHandle),
				},
				uncapturedErrorCallbackInfo = new WGPUUncapturedErrorCallbackInfo {
					callback = &uncapturedErrorCallback,
					userdata1 = (void*)GCHandle.ToIntPtr(stHandle),
				},
			};
			WGPURequestDeviceCallbackInfo cb = new() {
				mode = WGPUCallbackMode.AllowSpontaneous,
				callback = &deviceRequestCallback,
				userdata1 = (void*)GCHandle.ToIntPtr(reqHandle),
			};
			WGPUFuture future = wgpuAdapterRequestDevice(adapter, &desc, cb);
			req.Done.Wait();
			if (req.Status != WGPURequestDeviceStatus.Success || req.Object.IsNull)
				throw new WebgpuException("wgpuAdapterRequestDevice", req.Message ?? req.Status.ToString());
			callbackStateHandle = stHandle;
			stHandle = default;
			return req.Object;
		} finally {
			if (stHandle.IsAllocated)
				stHandle.Free();
			reqHandle.Free();
		}
	}

	[UnmanagedCallersOnly]
	private static void deviceRequestCallback(
		WGPURequestDeviceStatus status,
		WGPUDevice device,
		WGPUStringView message,
		void* userdata1,
		void* userdata2
	) {
		var h = GCHandle.FromIntPtr((nint)userdata1);
		var req = (Request<WGPURequestDeviceStatus, WGPUDevice>)h.Target!;
		req.Status = status;
		req.Object = device;
		req.Message = message.ToString();
		req.Done.Set();
	}

	[UnmanagedCallersOnly]
	private static void deviceLostCallback(
		WGPUDevice* device,
		WGPUDeviceLostReason reason,
		WGPUStringView message,
		void* userdata1,
		void* userdata2
	) {
		var h = GCHandle.FromIntPtr((nint)userdata1);
		var st = (CallbackState)h.Target!;
		if (Volatile.Read(ref st.Owner.disposed) != 0)
			return;
		// TODO: this just happens to rely on the fact that the enum members match with an offset of 1, it
		// needs something less fragile
		DeviceLossEventReason r = DeviceLossEventReason.Enum.FromTag((DeviceLossEventReason.Case)((int)reason - 1));
		st.Owner.NotifyLost(new DeviceLostInfo(DeviceLossInfoKind.Final, r, message.ToString()));
	}

	[UnmanagedCallersOnly]
	private static void uncapturedErrorCallback(
		WGPUDevice* device,
		WGPUErrorType type,
		WGPUStringView message,
		void* userdata1,
		void* userdata2
	) {
		var st = (CallbackState)GCHandle.FromIntPtr((nint)userdata1).Target!;
		GpuDevice owner = st.Owner;
		if (Volatile.Read(ref owner.disposed) != 0)
			return;
		GpuErrorKind kind = type switch {
			WGPUErrorType.Validation => GpuErrorKind.Validation,
			WGPUErrorType.OutOfMemory => GpuErrorKind.OutOfMemory,
			WGPUErrorType.Internal => GpuErrorKind.Internal,
			_ => GpuErrorKind.Unknown,
		};
		GpuError error = new(kind, message.ToString());
		errorsOnThread++;
		try {
			owner.errorHandler(owner, in error);
		} catch (Exception e) {
			Environment.FailFast($"GpuDevice error handler threw while handling WebGPU {kind} error '{error.Message}'; can't unwind here, aborting", e);
		}
	}

	// ==========================================================================
	// public api (core)

	/// <summary>
	/// Creates a command encoder for recording GPU commands.
	/// </summary>
	public GpuCommandEncoder CreateCommandEncoder() {
		chk();
		WGPUCommandEncoderDescriptor desc = default;
		return new GpuCommandEncoder(Check(wgpuDeviceCreateCommandEncoder(Device, &desc)), Limits);
	}

	/// <summary>
	/// Submits a command buffer to the queue, consuming it.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="commands"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="commands"/> has already been submitted or disposed.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if <paramref name="commands"/> isn't <see cref="GpuCommandBuffer.IsValid"/>.
	/// </exception>
	public void Submit(GpuCommandBuffer commands) {
		ArgumentNullException.ThrowIfNull(commands);
		Submit(new ReadOnlySpan<GpuCommandBuffer>(in commands));
	}

	/// <summary>
	/// Submits command buffers to the queue in order, consuming them.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if any element of <paramref name="commands"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if any element of <paramref name="commands"/> has already been submitted or disposed,
	/// or appears more than once.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if any element of <paramref name="commands"/> isn't
	/// <see cref="GpuCommandBuffer.IsValid"/>.
	/// </exception>
	/// <remarks>
	/// Equivalent to submitting each buffer separately, but cheaper.
	/// </remarks>
	public void Submit(ReadOnlySpan<GpuCommandBuffer> commands) {
		chk();
		var raw = new WGPUCommandBuffer[commands.Length];
		for (int i = 0; i < commands.Length; i++) {
			GpuCommandBuffer c = commands[i] ?? throw new ArgumentNullException(nameof(commands), "element is null");
			if (c.IsConsumed)
				throw new ArgumentException("command buffer has already been submitted or disposed", nameof(commands));
			if (!c.IsValid)
				throw new InvalidOperationException("command buffer is invalid, since WebGPU reported an error while it was being finished; see the device's error handler");
			for (int j = 0; j < i; j++)
				if (ReferenceEquals(commands[j], c))
					throw new ArgumentException("command buffer appears more than once", nameof(commands));
			raw[i] = c.WgpuCommandBuffer;
		}
		fixed (WGPUCommandBuffer* p = raw)
			wgpuQueueSubmit(Queue, (nuint)raw.Length, p);
		foreach (GpuCommandBuffer c in commands)
			c.Release();
		DispatchMapCallbacks();
	}

	/// <summary>
	/// Processes completed GPU work without blocking, and dispatches completed buffer map requests
	/// (see <see cref="GpuBuffer.BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})"/>) on the
	/// calling thread.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if all submitted work has completed; otherwise,
	/// <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// Typically called once per frame by code that has map requests in flight.
	/// </remarks>
	public bool Poll() {
		chk();
		bool idle = wgpuDevicePoll(Device, false, null);
		DispatchMapCallbacks();
		return idle;
	}

	/// <summary>
	/// Blocks until all submitted GPU work has completed, then dispatches completed buffer map
	/// requests like <see cref="Poll()"/>.
	/// </summary>
	public void WaitIdle() {
		chk();
		_ = wgpuDevicePoll(Device, true, null);
		DispatchMapCallbacks();
	}

	internal void EnqueueMapCompletion(MapRequest req) => mapCompletions.Enqueue(req);

	internal void DispatchMapCallbacks() {
		while (mapCompletions.TryDequeue(out MapRequest? req)) {
			req.Buffer.CompleteMap(req);
			req.Callback?.Invoke(req.Status);
		}
	}

	/// <summary>
	/// Creates a GPU buffer, returning an owning object.
	/// </summary>
	/// <param name="size">Buffer size in bytes.</param>
	/// <param name="usage">Declared WebGPU usage flags for the buffer.</param>
	/// <param name="mappedAtCreation">
	/// Whether the buffer should be mapped for <see cref="MapMode.Write"/> immediately on
	/// creation, which doesn't require <see cref="BufferUsage.MapWrite"/>. If so,
	/// <paramref name="size"/> must be a multiple of 4.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="mappedAtCreation"/> is <see langword="true"/> and
	/// <paramref name="size"/> isn't a multiple of 4.
	/// </exception>
	public GpuBuffer CreateBuffer(ulong size, BufferUsage usage, bool mappedAtCreation = false) {
		chk();
		if (mappedAtCreation && size % 4 != 0)
			throw new ArgumentException("size must be a multiple of 4 if mappedAtCreation is set", nameof(size));
		WGPUBufferDescriptor desc = new() {
			size = size,
			usage = usage.ToWebgpuType(),
			mappedAtCreation = mappedAtCreation,
		};
		return new GpuBuffer(this, Check(wgpuDeviceCreateBuffer(Device, &desc)), size, usage, mappedAtCreation);
	}

	/// <summary>
	/// Writes a single unmanaged value into a GPU buffer.
	/// </summary>
	/// <typeparam name="T">Unmanaged value type to upload.</typeparam>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="val">Value to upload.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="buffer"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="buffer"/> lacks <see cref="BufferUsage.CopyDst"/>,
	/// <paramref name="offset"/> or the number of bytes to write isn't a multiple of 4, or the
	/// written range is out of bounds.
	/// </exception>
	/// <remarks>
	/// This is a queue write, not a mapped-buffer write.
	/// </remarks>
	public void WriteToBuffer<T>(GpuBufferHandle buffer, ulong offset, in T val) where T : unmanaged {
		chk();
		requireWritable(buffer, offset, (ulong)sizeof(T));
		fixed (T* p = &val)
			wgpuQueueWriteBuffer(Queue, buffer.WgpuBuffer, offset, p, (nuint)sizeof(T));
	}

	/// <summary>
	/// Writes data from a span into a GPU buffer.
	/// </summary>
	/// <typeparam name="T">Unmanaged element type to upload.</typeparam>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="data">Data to upload.</param>
	/// <inheritdoc cref="WriteToBuffer{T}(GpuBufferHandle, ulong, in T)" path="/exception"/>
	/// <remarks>
	/// This is a queue write, not a mapped-buffer write. Empty spans are accepted and are a no-op.
	/// </remarks>
	public void WriteToBuffer<T>(GpuBufferHandle buffer, ulong offset, ReadOnlySpan<T> data) where T : unmanaged {
		chk();
		ArgumentNullException.ThrowIfNull(buffer);
		if (data.IsEmpty)
			return;
		requireWritable(buffer, offset, (ulong)data.Length * (ulong)sizeof(T));
		fixed (T* p = data)
			wgpuQueueWriteBuffer(Queue, buffer.WgpuBuffer, offset, p, (nuint)(data.Length * sizeof(T)));
	}

	/// <summary>
	/// Writes data from a pointer into a GPU buffer.
	/// </summary>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="data">Pointer to the data to upload.</param>
	/// <param name="size">Number of bytes to upload from <paramref name="data"/>.</param>
	/// <inheritdoc cref="WriteToBuffer{T}(GpuBufferHandle, ulong, in T)" path="/exception"/>
	/// <remarks>
	/// This is a queue write, not a mapped-buffer write. <paramref name="data"/> need only remain
	/// valid for the duration of the call.
	/// </remarks>
	public void WriteToBuffer(GpuBufferHandle buffer, ulong offset, void* data, nuint size) {
		chk();
		requireWritable(buffer, offset, size);
		wgpuQueueWriteBuffer(Queue, buffer.WgpuBuffer, offset, data, size);
	}

	[StackTraceHidden]
	private static void requireWritable(GpuBufferHandle buffer, ulong offset, ulong size) {
		ArgumentNullException.ThrowIfNull(buffer);
		if (buffer.Usage.HasNone(BufferUsage.CopyDst))
			throw new ArgumentException("buffer must have CopyDst set in its usages", nameof(buffer));
		if (offset % 4 != 0)
			throw new ArgumentException("must be a multiple of 4", nameof(offset));
		if (size % 4 != 0)
			throw new ArgumentException("the number of bytes to write must be a multiple of 4");
		if (offset > buffer.Size || size > buffer.Size - offset)
			throw new ArgumentException("written range is out of the buffer's bounds", nameof(offset));
	}

	/// <summary>
	/// Creates a texture and its default view, returning an owning object.
	/// </summary>
	/// <param name="params">Texture creation parameters.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the provided <see cref="GpuTextureCreateParams.ViewFormats"/> contains the texture's
	/// <see cref="GpuTextureCreateParams.Format"/> or any duplicates.
	/// </exception>
	public GpuTexture CreateTexture(in GpuTextureCreateParams @params) {
		chk();

		ReadOnlySpan<TextureFormat> viewFormats = @params.ViewFormats.IsDefault
			? ReadOnlySpan<TextureFormat>.Empty
			: @params.ViewFormats.AsSpan();
		WGPUTextureFormat[] wgpuViewFormats;
		if (viewFormats.Length > 0) {
			wgpuViewFormats = new WGPUTextureFormat[viewFormats.Length];
			HashSet<TextureFormat> tmp = new(viewFormats.Length);
			for (int i = 0; i < viewFormats.Length; i++) {
				if (viewFormats[i] == @params.Format)
					throw new ArgumentException("ViewFormats must not contain the texture's format", nameof(@params));
				if (!tmp.Add(viewFormats[i]))
					throw new ArgumentException("ViewFormats must not contain duplicates", nameof(@params));
				wgpuViewFormats[i] = viewFormats[i].ToWebgpuType();
			}
		}

		fixed (WGPUTextureFormat* p = wgpuViewFormats) {
			WGPUTextureDescriptor desc = new() {
				size = new WGPUExtent3D {
					width = @params.Width,
					height = @params.Height,
					depthOrArrayLayers = @params.DepthOrArrayLayers,
				},
				mipLevelCount = @params.MipLevelCount,
				sampleCount = @params.SampleCount,
				dimension = @params.Dimension.ToWebgpuType(),
				format = @params.Format.ToWebgpuType(),
				usage = @params.Usage.ToWebgpuType(),
				viewFormatCount = (nuint)viewFormats.Length,
				viewFormats = p,
			};
			WGPUTexture tex = Check(wgpuDeviceCreateTexture(Device, &desc));
			try {
				return new GpuTexture(
					tex,
					@params.Width,
					@params.Height,
					@params.DepthOrArrayLayers,
					@params.MipLevelCount,
					@params.SampleCount,
					@params.Dimension,
					@params.Format,
					@params.Usage,
					viewFormats.ToArray()
				);
			} catch (WebgpuException) {
				wgpuTextureRelease(tex);
				throw;
			}
		}
	}

	/// <summary>
	/// Writes texel data into a texture.
	/// </summary>
	/// <typeparam name="T">Unmanaged source element type.</typeparam>
	/// <param name="tex">Destination texture.</param>
	/// <param name="dst">Destination texture region and subresource.</param>
	/// <param name="data">Source texel data.</param>
	/// <param name="layout">Source memory layout.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="tex"/> lacks <see cref="TextureUsage.CopyDst"/>, or
	/// <paramref name="dst"/> names a mip level that doesn't exist or is out of its bounds.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The number of bytes consumed from each source row is determined by the uploaded texture region
	/// and format, while <see cref="GpuTextureLayout.BytesPerRow"/> is the spacing between row starts
	/// in memory. This allows uploading from data with padding between rows.
	/// </para>
	/// <para>
	/// There are no alignment/etc. restrictions on the values in <see cref="GpuTextureLayout"/>.
	/// </para>
	/// <para>
	/// Empty spans are accepted and are a no-op.
	/// </para>
	/// </remarks>
	public void WriteToTexture<T>(
		GpuTextureHandle tex,
		in GpuTextureRegion dst,
		ReadOnlySpan<T> data,
		in GpuTextureLayout layout
	) where T : unmanaged {
		chk();
		requireTextureWritable(tex, dst);
		if (data.IsEmpty)
			return;
		fixed (T* p = data)
			WriteToTexture(tex, dst, p, (nuint)(data.Length * sizeof(T)), layout);
	}

	/// <summary>
	/// Writes texel data into a texture using the queue.
	/// </summary>
	/// <param name="tex">Destination texture.</param>
	/// <param name="dst">Destination texture region and subresource.</param>
	/// <param name="data">Pointer to the source texel data.</param>
	/// <param name="size">Number of bytes to upload from <paramref name="data"/>.</param>
	/// <param name="layout">Source memory layout.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="tex"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="tex"/> lacks <see cref="TextureUsage.CopyDst"/>, or
	/// <paramref name="dst"/> names a mip level that doesn't exist or is out of its bounds.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The number of bytes consumed from each source row is determined by the uploaded texture region
	/// and format, while <see cref="GpuTextureLayout.BytesPerRow"/> is the spacing between row starts
	/// in memory. This allows uploading from data with padding between rows.
	/// </para>
	/// <para>
	/// There are no alignment/etc. restrictions on the values in <see cref="GpuTextureLayout"/>.
	/// </para>
	/// <para>
	/// <paramref name="data"/> need only remain valid for the duration of the call.
	/// </para>
	/// </remarks>
	public void WriteToTexture(
		GpuTextureHandle tex,
		in GpuTextureRegion dst,
		void* data,
		nuint size,
		in GpuTextureLayout layout
	) {
		chk();
		requireTextureWritable(tex, dst);
		WGPUTexelCopyTextureInfo copyDst = new() {
			texture = tex.WgpuTexture,
			mipLevel = dst.MipLevel,
			origin = new WGPUOrigin3D {
				x = dst.X,
				y = dst.Y,
				z = dst.Z,
			},
			aspect = dst.Aspect.ToWebgpuType(),
		};
		WGPUTexelCopyBufferLayout dataLayout = new() {
			offset = layout.Offset,
			bytesPerRow = layout.BytesPerRow,
			rowsPerImage = layout.RowsPerImage,
		};
		WGPUExtent3D texSize = new() {
			width = dst.Width,
			height = dst.Height,
			depthOrArrayLayers = dst.DepthOrArrayLayers,
		};
		wgpuQueueWriteTexture(Queue, &copyDst, data, size, &dataLayout, &texSize);
	}

	[StackTraceHidden]
	private static void requireTextureWritable(GpuTextureHandle tex, in GpuTextureRegion dst) {
		ArgumentNullException.ThrowIfNull(tex);
		if (tex.Usage.HasNone(TextureUsage.CopyDst))
			throw new ArgumentException("texture must have CopyDst set in its usages", nameof(tex));
		tex.RequireRegionInBounds(dst, nameof(dst));
	}

	/// <summary>
	/// Creates a sampler, returning an owning object.
	/// </summary>
	/// <param name="params">Sampler creation parameters.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <see cref="GpuSamplerCreateParams.LodMinClamp"/> is negative,
	/// <see cref="GpuSamplerCreateParams.LodMaxClamp"/> is smaller than it, or
	/// <see cref="GpuSamplerCreateParams.MaxAnisotropy"/> is 0.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <see cref="GpuSamplerCreateParams.MaxAnisotropy"/> is larger than 1 and any of the
	/// filter modes are set to anything but linear filtering.
	/// </exception>
	public GpuSampler CreateSampler(in GpuSamplerCreateParams @params) {
		chk();
		if (@params.LodMinClamp < 0)
			throw new ArgumentOutOfRangeException(nameof(@params), "LodMinClamp cannot be negative");
		if (@params.LodMaxClamp < @params.LodMinClamp)
			throw new ArgumentOutOfRangeException(nameof(@params), "LodMaxClamp cannot be smaller than LodMinClamp");
		if (@params.MaxAnisotropy < 1)
			throw new ArgumentOutOfRangeException(nameof(@params), "MaxAnisotropy must be at least 1");
		if (
			@params.MaxAnisotropy > 1
			&& (
				@params.MinFilter != FilterMode.Linear
				|| @params.MagFilter != FilterMode.Linear
				|| @params.MipmapFilter != MipmapFilterMode.Linear
			)
		)
			throw new ArgumentException("MinFilter/MagFilter/MipMapFilter must be set to Linear if MaxAnisotropy > 1", nameof(@params));
		WGPUSamplerDescriptor desc = new() {
			addressModeU = @params.AddressModeU.ToWebgpuType(),
			addressModeV = @params.AddressModeV.ToWebgpuType(),
			addressModeW = @params.AddressModeW.ToWebgpuType(),
			magFilter = @params.MagFilter.ToWebgpuType(),
			minFilter = @params.MinFilter.ToWebgpuType(),
			mipmapFilter = @params.MipmapFilter.ToWebgpuType(),
			lodMinClamp = @params.LodMinClamp,
			lodMaxClamp = @params.LodMaxClamp,
			compare = @params.Compare.ToWebgpuType(),
			maxAnisotropy = @params.MaxAnisotropy,
		};
		return new GpuSampler(Check(wgpuDeviceCreateSampler(Device, &desc)));
	}

	private static WGPUBindGroupLayoutEntry toRawBindGroupLayoutEntry(in GpuBindGroupLayoutEntry entry) {
		WGPUBindGroupLayoutEntry raw = new() {
			binding = entry.Binding,
			visibility = entry.Visibility.ToWebgpuType(),
		};
		switch (entry.Layout) {
		case GpuBufferBindingLayout b:
			raw.buffer = new WGPUBufferBindingLayout {
				type = b.Type.ToWebgpuType(),
				hasDynamicOffset = b.HasDynamicOffset,
				minBindingSize = b.MinBindingSize,
			};
			return raw;
		case GpuSamplerBindingLayout s:
			raw.sampler = new WGPUSamplerBindingLayout {
				type = s.Type.ToWebgpuType(),
			};
			return raw;
		case GpuStorageTextureBindingLayout st:
			raw.storageTexture = new WGPUStorageTextureBindingLayout {
				access = st.Access.ToWebgpuType(),
				format = st.Format.ToWebgpuType(),
				viewDimension = st.ViewDimension.ToWebgpuType(),
			};
			return raw;
		case GpuTextureBindingLayout t:
			raw.texture = new WGPUTextureBindingLayout {
				sampleType = t.SampleType.ToWebgpuType(),
				viewDimension = t.ViewDimension.ToWebgpuType(),
				multisampled = t.Multisampled,
			};
			return raw;
		default:
			throw InternalStateException.BadClosedHierarchy(entry.Layout);
		}
	}

	/// <summary>
	/// Creates a bind group layout from the given entries, returning an owning object.
	/// </summary>
	/// <param name="entries">Entries describing the bindings in the layout.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if any entry has a <see langword="null"/> layout.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="entries"/> is empty or contains duplicate binding indices.
	/// </exception>
	public GpuBindGroupLayout CreateBindGroupLayout(ReadOnlySpan<GpuBindGroupLayoutEntry> entries) {
		chk();
		if (entries.IsEmpty)
			throw new ArgumentException("bind group layout must contain at least one entry", nameof(entries));

		for (int i = 0; i < entries.Length; i++) {
			ref readonly GpuBindGroupLayoutEntry e = ref entries[i];
			ArgumentNullException.ThrowIfNull(e.Layout);

			// O(n^2) on paper but it's honestly probably better than allocation/etc for a hashset when
			// there's probably gonna be like only a few entries 99% of the time
			for (int j = 0; j < i; j++)
				if (entries[j].Binding == e.Binding)
					throw new ArgumentException($"duplicate bind group layout binding {e.Binding}", nameof(entries));
		}

		WGPUBindGroupLayoutEntry* rawEntries = stackalloc WGPUBindGroupLayoutEntry[entries.Length];
		for (int i = 0; i < entries.Length; i++)
			rawEntries[i] = toRawBindGroupLayoutEntry(entries[i]);
		WGPUBindGroupLayoutDescriptor desc = new() {
			entryCount = (nuint)entries.Length,
			entries = rawEntries,
		};
		List<(uint Binding, BufferBindingType Type)> dynamic = new();
		foreach (GpuBindGroupLayoutEntry e in entries)
			if (e.Layout is GpuBufferBindingLayout { HasDynamicOffset: true } b)
				dynamic.Add((e.Binding, b.Type));
		dynamic.Sort(static (x, y) => x.Binding.CompareTo(y.Binding));
		BufferBindingType[] dynamicBindings = dynamic.ConvertAll(static d => d.Type).ToArray();
		return new GpuBindGroupLayout(Check(wgpuDeviceCreateBindGroupLayout(Device, &desc)), dynamicBindings);
	}

	private static WGPUBindGroupEntry toRawBindGroupEntry(in GpuBindGroupEntry entry) {
		WGPUBindGroupEntry raw = new() {
			binding = entry.Binding,
		};
		switch (entry.Resource) {
		case GpuBufferBindingResource b:
			raw.buffer = b.Buffer.WgpuBuffer;
			raw.offset = b.Offset;
			raw.size = b.Size ?? b.Buffer.Size - b.Offset;
			return raw;
		case GpuSamplerBindingResource s:
			raw.sampler = s.Sampler.WgpuSampler;
			return raw;
		case GpuTextureViewBindingResource v:
			raw.textureView = v.View.WgpuTextureView;
			return raw;
		default:
			throw InternalStateException.BadClosedHierarchy(entry.Resource);
		}
	}

	/// <summary>
	/// Creates a bind group from the given layout and entries, returning an owning object.
	/// </summary>
	/// <param name="layout">Bind group layout the created bind group must satisfy.</param>
	/// <param name="entries">Binding entries to populate in the bind group.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="layout"/> is <see langword="null"/> or if any entry contains a
	/// <see langword="null"/> resource.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="entries"/> is empty, contains duplicate binding indices, or contains
	/// a buffer binding whose range is empty or out of bounds.
	/// </exception>
	/// <remarks>
	/// The caller is responsible for providing entries compatible with <paramref name="layout"/>. This
	/// method catches obvious bugs such as duplicate bindings and invalid buffer ranges, but makes no
	/// attempt to validate layout compatibility.
	/// </remarks>
	public GpuBindGroup CreateBindGroup(GpuBindGroupLayoutHandle layout, ReadOnlySpan<GpuBindGroupEntry> entries) {
		chk();
		ArgumentNullException.ThrowIfNull(layout);
		if (entries.IsEmpty)
			throw new ArgumentException("bind group must contain at least one entry", nameof(entries));

		for (int i = 0; i < entries.Length; i++) {
			ref readonly GpuBindGroupEntry e = ref entries[i];
			ArgumentNullException.ThrowIfNull(e.Resource);

			// O(n^2) on paper but it's honestly probably better than allocation/etc for a hashset when
			// there's probably gonna be like only a few entries 99% of the time
			for (int j = 0; j < i; j++)
				if (entries[j].Binding == e.Binding)
					throw new ArgumentException($"duplicate bind group binding {e.Binding}", nameof(entries));

			switch (e.Resource) {
			case GpuBufferBindingResource b:
				ArgumentNullException.ThrowIfNull(b.Buffer);
				if (b.Offset > b.Buffer.Size)
					throw new ArgumentException($"buffer binding {e.Binding} has an offset past the end of the buffer", nameof(entries));
				ulong size = b.Size ?? b.Buffer.Size - b.Offset;
				if (size == 0)
					throw new ArgumentException($"buffer binding {e.Binding} must expose a nonzero range", nameof(entries));
				if (b.Offset + size > b.Buffer.Size)
					throw new ArgumentException($"buffer binding {e.Binding} range extends past the end of the buffer", nameof(entries));
				break;
			case GpuSamplerBindingResource s:
				ArgumentNullException.ThrowIfNull(s.Sampler);
				break;
			case GpuTextureViewBindingResource v:
				ArgumentNullException.ThrowIfNull(v.View);
				break;
			default:
				throw InternalStateException.BadClosedHierarchy(e.Resource);
			}
		}

		WGPUBindGroupEntry* rawEntries = stackalloc WGPUBindGroupEntry[entries.Length];
		for (int i = 0; i < entries.Length; i++)
			rawEntries[i] = toRawBindGroupEntry(entries[i]);
		WGPUBindGroupDescriptor desc = new() {
			layout = layout.WgpuBindGroupLayout,
			entryCount = (nuint)entries.Length,
			entries = rawEntries,
		};
		return new GpuBindGroup(Check(wgpuDeviceCreateBindGroup(Device, &desc)), layout.DynamicBindings);
	}

	/// <summary>
	/// Creates a shader module from WGSL source code, returning an owning object.
	/// </summary>
	/// <param name="source">WGSL source code.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="source"/> is empty.
	/// </exception>
	public GpuShaderModule CreateShaderModuleWgsl(ReadOnlySpan<char> source) {
		chk();
		if (source.IsEmpty)
			throw new ArgumentException("WGSL source code must not be empty", nameof(source));

		int bytes = Encoding.UTF8.GetByteCount(source);
		Span<byte> utf8 = bytes <= 1024 ? stackalloc byte[bytes] : new byte[bytes];
		int written = Encoding.UTF8.GetBytes(source, utf8);
		fixed (byte* p = utf8) {
			WGPUShaderSourceWGSL src = new() {
				chain = new WGPUChainedStruct {
					sType = WGPUSType.ShaderSourceWGSL,
					next = null,
				},
				code = new WGPUStringView(p, written),
			};
			WGPUShaderModuleDescriptor desc = new() {
				nextInChain = &src.chain,
			};
			return new GpuShaderModule(Check(wgpuDeviceCreateShaderModule(Device, &desc)));
		}
	}

	/// <summary>
	/// Creates a shader module from SPIR-V code, returning an owning object.
	/// </summary>
	/// <param name="code">SPIR-V code as 32-bit words.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="code"/> is shorter than the 5-word SPIR-V header or doesn't start
	/// with the SPIR-V magic number, including if its words are byte-swapped, e.g. because a SPIR-V
	/// file was read with the wrong byte order.
	/// </exception>
	/// <remarks>
	/// Only the header is checked; the rest of the code is validated by WebGPU, which reports
	/// problems to the device's <see cref="GpuDeviceOptions.ErrorHandler"/>.
	/// </remarks>
	public GpuShaderModule CreateShaderModuleSpirv(ReadOnlySpan<uint> code) {
		chk();
		if (code.Length < spirvHeaderWords)
			throw new ArgumentException($"SPIR-V code must be at least {spirvHeaderWords} words (its header), but is {code.Length}", nameof(code));
		if (code[0] != spirvMagic) {
			if (BinaryPrimitives.ReverseEndianness(code[0]) == spirvMagic)
				throw new ArgumentException("SPIR-V words are byte-swapped; the code was likely read from a file with the wrong byte order", nameof(code));
			throw new ArgumentException($"code isn't SPIR-V; it starts with 0x{code[0]:x8} instead of the magic number 0x{spirvMagic:x8}", nameof(code));
		}

		fixed (uint* p = code) {
			WGPUShaderSourceSPIRV src = new() {
				chain = new WGPUChainedStruct {
					sType = WGPUSType.ShaderSourceSPIRV,
					next = null,
				},
				codeSize = (uint)code.Length,
				code = p,
			};
			WGPUShaderModuleDescriptor desc = new() {
				nextInChain = &src.chain,
			};
			return new GpuShaderModule(Check(wgpuDeviceCreateShaderModule(Device, &desc)));
		}
	}

	/// <summary>
	/// Creates a shader module from SPIR-V code, returning an owning object.
	/// </summary>
	/// <param name="code">
	/// SPIR-V code as 32-bit words encoded in little-endian byte order. Bytes 0..3 encode word 0,
	/// bytes 4..7 encode word 1, and so on.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown if the length of <paramref name="code"/> is not a multiple of 4, it's shorter than the
	/// 5-word SPIR-V header, or it doesn't start with the SPIR-V magic number. Big-endian SPIR-V is
	/// rejected with a message saying so.
	/// </exception>
	/// <inheritdoc cref="CreateShaderModuleSpirv(ReadOnlySpan{uint})" path="/remarks"/>
	public GpuShaderModule CreateShaderModuleSpirv(ReadOnlySpan<byte> code) {
		chk();
		if ((code.Length & 0b11) != 0)
			throw new ArgumentException("byte length of SPIR-V code encoded as bytes must be a multiple of 4", nameof(code));
		if (code.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(code) == spirvMagic)
			throw new ArgumentException("SPIR-V code is big-endian, but this overload expects little-endian bytes", nameof(code));

		Span<uint> words = code.Length <= 1024 ? stackalloc uint[code.Length >> 2] : new uint[code.Length >> 2];
		for (int i = 0; i < words.Length; i++)
			words[i] = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(i << 2, 4));
		return CreateShaderModuleSpirv(words);
	}

	/// <summary>
	/// Creates a pipeline layout from the given bind group layouts, returning an owning object.
	/// </summary>
	/// <param name="layouts">
	/// Bind group layouts in bind group order; may be empty for pipelines whose shaders have no
	/// bindings.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if an element of <paramref name="layouts"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The order of <paramref name="layouts"/> determines the bind group indices expected by
	/// pipelines created from the returned layout.
	/// </remarks>
	public GpuPipelineLayout CreatePipelineLayout(ReadOnlySpan<GpuBindGroupLayoutHandle> layouts) {
		chk();

		WGPUBindGroupLayout* bgLayouts = stackalloc WGPUBindGroupLayout[layouts.Length];
		for (int i = 0; i < layouts.Length; i++) {
			ArgumentNullException.ThrowIfNull(layouts[i]);
			bgLayouts[i] = layouts[i].WgpuBindGroupLayout;
		}

		WGPUPipelineLayoutDescriptor desc = new() {
			bindGroupLayoutCount = (nuint)layouts.Length,
			bindGroupLayouts = bgLayouts,
		};
		return new GpuPipelineLayout(Check(wgpuDeviceCreatePipelineLayout(Device, &desc)));
	}

	/// <summary>
	/// Creates a render pipeline, returning an owning object.
	/// </summary>
	/// <param name="params">Render pipeline creation parameters.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if the layout or a shader module is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if an entry point name is empty, the fragment state has no color targets, or the strip
	/// index format doesn't match the topology (see <see cref="PrimitiveState.StripIndexFormat"/>).
	/// </exception>
	/// <remarks>
	/// Compatibility between the shaders, the layout, and the vertex/target formats is only validated
	/// by WebGPU.
	/// </remarks>
	public GpuRenderPipeline CreateRenderPipeline(in GpuRenderPipelineCreateParams @params) {
		// -----------------------------------------------------------------
		// validation
		chk();

		ArgumentNullException.ThrowIfNull(@params.Layout);
		ArgumentNullException.ThrowIfNull(@params.Vertex.ShaderModule);
		ArgumentException.ThrowIfNullOrWhiteSpace(@params.Vertex.EntryPoint);

		bool haveFrag = false;
		VertexState vert = @params.Vertex;
		FragmentState frag = @params.Fragment ?? default;
		PrimitiveState prim = @params.Primitive ?? new PrimitiveState();
		MultisampleState ms = @params.Multisample ?? new MultisampleState();

		if (@params.Fragment is FragmentState f) {
			haveFrag = true;
			frag = f;
			ArgumentNullException.ThrowIfNull(f.ShaderModule);
			ArgumentException.ThrowIfNullOrWhiteSpace(f.EntryPoint);
			if (f.Targets.IsDefaultOrEmpty)
				throw new ArgumentException("fragment state must have at least one color target", nameof(@params));
		}

		bool stripTopo = prim.Topology.Tag is PrimitiveTopology.Case.LineStrip or PrimitiveTopology.Case.TriangleStrip;
		if (!stripTopo && prim.StripIndexFormat != IndexFormat.Undefined)
			throw new ArgumentException("strip index format is only valid for strip topologies", nameof(@params));

		// -----------------------------------------------------------------
		// upfront allocations, as a workaround for CS8346
		ReadOnlySpan<VertexBufferLayout> vertBuffers = vert.Buffers.IsDefault ? ReadOnlySpan<VertexBufferLayout>.Empty : vert.Buffers.AsSpan();
		int totalVertAttrCount = 0;
		for (int i = 0; i < vertBuffers.Length; i++)
			totalVertAttrCount += vertBuffers[i].Attributes.Length;

		ReadOnlySpan<ColorTargetState> colorTargets = haveFrag && !frag.Targets.IsDefault ? frag.Targets.AsSpan() : ReadOnlySpan<ColorTargetState>.Empty;

		WGPUVertexBufferLayout* wgpuVertBuffers = stackalloc WGPUVertexBufferLayout[vertBuffers.Length];
		WGPUVertexAttribute* wgpuVertAttrs = stackalloc WGPUVertexAttribute[totalVertAttrCount];
		WGPUColorTargetState* wgpuColorTargets = stackalloc WGPUColorTargetState[colorTargets.Length];
		WGPUBlendState* wgpuBlendStates = stackalloc WGPUBlendState[colorTargets.Length];

		int vsEntrySize = Encoding.UTF8.GetByteCount(vert.EntryPoint);
		byte* vsEntry = stackalloc byte[vsEntrySize];
		int vsEntryLen = Encoding.UTF8.GetBytes(vert.EntryPoint, new Span<byte>(vsEntry, vsEntrySize));

		int fsEntrySize = haveFrag ? Encoding.UTF8.GetByteCount(frag.EntryPoint) : 0;
		byte* fsEntry = stackalloc byte[fsEntrySize];
		int fsEntryLen = haveFrag ? Encoding.UTF8.GetBytes(frag.EntryPoint, new Span<byte>(fsEntry, fsEntrySize)) : 0;

		// -----------------------------------------------------------------
		// vertex
		int attrBase = 0;
		for (int i = 0; i < vertBuffers.Length; i++) {
			VertexBufferLayout vb = vertBuffers[i];
			ReadOnlySpan<VertexAttribute> vbAttributes = vb.Attributes.IsDefault ? ReadOnlySpan<VertexAttribute>.Empty : vb.Attributes.AsSpan();
			for (int j = 0; j < vbAttributes.Length; j++)
				wgpuVertAttrs[attrBase + j] = vbAttributes[j].ToWebgpuType();
			wgpuVertBuffers[i] = new WGPUVertexBufferLayout {
				arrayStride = vb.ArrayStride,
				stepMode = vb.StepMode.ToWebgpuType(),
				attributeCount = (nuint)vbAttributes.Length,
				attributes = vbAttributes.IsEmpty ? null : wgpuVertAttrs + attrBase,
			};
			attrBase += vbAttributes.Length;
		}

		WGPUVertexState wgpuVert = new() {
			module = vert.ShaderModule.WgpuShaderModule,
			entryPoint = new WGPUStringView(vsEntry, vsEntryLen),
			constantCount = 0, // TODO
			constants = null, // TODO
			bufferCount = (nuint)vertBuffers.Length,
			buffers = vertBuffers.IsEmpty ? null : wgpuVertBuffers,
		};

		// -----------------------------------------------------------------
		// fragment
		WGPUFragmentState wgpuFrag = default;
		WGPUFragmentState* pWgpuFrag = null;
		if (haveFrag) {
			for (int i = 0; i < colorTargets.Length; i++)
				wgpuColorTargets[i] = colorTargets[i].ToWebgpuType(&wgpuBlendStates[i]);
			wgpuFrag = new WGPUFragmentState {
				module = frag.ShaderModule.WgpuShaderModule,
				entryPoint = new WGPUStringView(fsEntry, fsEntryLen),
				constantCount = 0, // TODO
				constants = null, // TODO
				targetCount = (nuint)colorTargets.Length,
				targets = colorTargets.IsEmpty ? null : wgpuColorTargets,
			};
			pWgpuFrag = &wgpuFrag;
		}

		// -----------------------------------------------------------------
		// primitive, depth/stencil, multisample
		WGPUPrimitiveState wgpuPrim = prim.ToWebgpuType();

		WGPUDepthStencilState wgpuDepthStencil = default;
		WGPUDepthStencilState* pWgpuDepthStencil = null;
		if (@params.DepthStencil is DepthStencilState ds) {
			wgpuDepthStencil = ds.ToWebgpuType();
			pWgpuDepthStencil = &wgpuDepthStencil;
		}

		WGPUMultisampleState wgpuMs = ms.ToWebgpuType();

		// -----------------------------------------------------------------
		// full pipeline descriptor
		WGPURenderPipelineDescriptor desc = new() {
			layout = @params.Layout.WgpuPipelineLayout,
			vertex = wgpuVert,
			primitive = wgpuPrim,
			depthStencil = pWgpuDepthStencil,
			multisample = wgpuMs,
			fragment = pWgpuFrag,
		};
		return new GpuRenderPipeline(Check(wgpuDeviceCreateRenderPipeline(Device, &desc)));
	}

	/// <summary>
	/// Releases all owned GPU state and invalidates all objects created from this
	/// <see cref="GpuDevice"/>.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;

		lock (attachmentsLock) {
			foreach (IDisposable a in attachments.Values)
				a.Dispose();
			attachments.Clear();
		}
		wgpuQueueRelease(Queue);
		wgpuDeviceRelease(Device);
		wgpuAdapterRelease(Adapter);
		wgpuInstanceRelease(Instance);
		// after the releases, in case WebGPU calls back during them; the callbacks check disposed
		callbackStateHandle.Free();
	}

	// ==========================================================================
	// public api (sugar/convenience)

	/// <summary>
	/// Creates a bind group layout with a single uniform buffer entry, returning an owning object;
	/// shorthand for <see cref="CreateBindGroupLayout(ReadOnlySpan{GpuBindGroupLayoutEntry})"/>.
	/// </summary>
	/// <param name="visibility">Shader stages that can see the uniform.</param>
	/// <param name="minBindingSize">
	/// Minimum size of the bound range in bytes; 0 means no minimum.
	/// </param>
	/// <param name="hasDynamicOffset">
	/// Whether the binding takes a dynamic offset; see
	/// <see cref="RenderPass.SetBindGroup(uint, GpuBindGroupHandle, ReadOnlySpan{uint})"/>.
	/// </param>
	/// <param name="binding">Binding index, i.e. <c>@binding(binding)</c> in WGSL.</param>
	/// <remarks>
	/// Pairs with
	/// <see cref="CreateUniformBufferBindGroup(GpuBindGroupLayoutHandle, GpuBufferHandle, ulong, ulong?, uint)"/>
	/// for the common case of binding a single uniform, e.g. shader parameters.
	/// </remarks>
	public GpuBindGroupLayout CreateUniformBufferBindGroupLayout(
		ShaderStage visibility,
		ulong minBindingSize = 0,
		bool hasDynamicOffset = false,
		uint binding = 0
	) =>
		CreateBindGroupLayout(
			[
				new GpuBindGroupLayoutEntry(
					Binding: binding,
					Visibility: visibility,
					Layout: new GpuBufferBindingLayout(
						Type: BufferBindingType.Uniform,
						HasDynamicOffset: hasDynamicOffset,
						MinBindingSize: minBindingSize
					)
				),
			]
		);

	/// <summary>
	/// Creates a bind group with a single uniform buffer entry, returning an owning object; shorthand
	/// for <see cref="CreateBindGroup(GpuBindGroupLayoutHandle, ReadOnlySpan{GpuBindGroupEntry})"/>.
	/// </summary>
	/// <param name="layout">Layout to create the bind group for.</param>
	/// <param name="buffer">
	/// Uniform buffer to bind; must have <see cref="BufferUsage.Uniform"/>.
	/// </param>
	/// <param name="offset">Byte offset of the bound range.</param>
	/// <param name="size">
	/// Size of the bound range in bytes; <see langword="null"/> means up to the end of the buffer.
	/// For a binding with a dynamic offset, this is the size of one dynamically offset range.
	/// </param>
	/// <param name="binding">Binding index, i.e. <c>@binding(binding)</c> in WGSL.</param>
	/// <inheritdoc cref="CreateBindGroup(GpuBindGroupLayoutHandle, ReadOnlySpan{GpuBindGroupEntry})" path="/exception"/>
	/// <remarks>
	/// Pairs with <see cref="CreateUniformBufferBindGroupLayout(ShaderStage, ulong, bool, uint)"/>.
	/// </remarks>
	public GpuBindGroup CreateUniformBufferBindGroup(
		GpuBindGroupLayoutHandle layout,
		GpuBufferHandle buffer,
		ulong offset = 0,
		ulong? size = null,
		uint binding = 0
	) =>
		CreateBindGroup(
			layout,
			[
				new GpuBindGroupEntry(
					Binding: binding,
					Resource: new GpuBufferBindingResource(
						Buffer: buffer,
						Offset: offset,
						Size: size
					)
				),
			]
		);

	// ==========================================================================
	// internal api surface / hooks

	// lets other parts of the engine (e.g. Draw) keep per-device state that lives exactly as long
	// as the device, without the device knowing about them
	internal T GetOrAttach<T>(Func<GpuDevice, T> factory) where T : class, IDisposable {
		chk();
		lock (attachmentsLock) {
			if (attachments.TryGetValue(typeof(T), out IDisposable? existing))
				return (T)existing;
			T created = factory(this);
			attachments.Add(typeof(T), created);
			return created;
		}
	}


	/// <summary>
	/// Notifies this <see cref="GpuDevice"/> that its underlying <see cref="WGPUDevice"/> has been
	/// lost.
	/// </summary>
	internal void NotifyLost(DeviceLostInfo info) {
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("attempted to NotifyLost() after dispose");
		lostInfo = info;
		Volatile.Write(ref lost, 1); // fence for the above one
	}

	/// <summary>
	/// Throws a <see cref="DeviceLostException"/> with the currently stored device loss information.
	/// Never returns; throws <see cref="InternalStateException"/> if the device hasn't been lost.
	/// </summary>
	[DoesNotReturn]
	internal void ThrowLostException() {
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("ThrowLostException() called post-dispose");
		if (Volatile.Read(ref lost) == 0)
			throw new InternalStateException("ThrowLostException() called while the device isn't lost");
		DeviceLostInfo info = Volatile.Read(ref lostInfo) ?? throw new InternalStateException("device lost but no DeviceLostInfo present");
		throw new DeviceLostException(info);
	}

	// ==========================================================================
	// lifetime/dispose
	private void chk() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		if (Volatile.Read(ref lost) != 0) {
			DeviceLostInfo info = Volatile.Read(ref lostInfo) ?? throw new InternalStateException("device lost but no DeviceLostInfo present");
			throw new DeviceLostException(info);
		}
	}
}
