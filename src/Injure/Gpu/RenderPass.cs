// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Records the commands of one render pass, opened with
/// <see cref="GpuCommandEncoderHandle.BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)"/>
/// or one of its shorthands.
/// </summary>
/// <remarks>
/// <para>
/// Dispose the pass to end it; its encoder can't record anything else until then. Pipeline, bind
/// group, buffer, and dynamic state set on a pass doesn't carry over to other passes.
/// </para>
/// <para>
/// Not thread-safe. Methods called after disposal throw <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public sealed unsafe class RenderPass : IDisposable {
	private const ulong wholeSize = ulong.MaxValue;
	private readonly WGPURenderPassEncoder passEnc;
	private readonly Action onFinished;
	private readonly GpuLimits limits;
	private readonly uint width;
	private readonly uint height;
	private bool pipelineSet = false;
	private bool indexBufferSet = false;
	private bool disposed = false;

	internal RenderPass(WGPURenderPassEncoder passEnc, Action onFinished, GpuLimits limits, uint width, uint height) {
		this.limits = limits;
		this.width = width;
		this.height = height;
		this.passEnc = passEnc;
		this.onFinished = onFinished;
	}

	/// <summary>
	/// Sets the pipeline used by subsequent draws.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="pipeline"/> is <see langword="null"/>.
	/// </exception>
	public void SetPipeline(GpuRenderPipelineHandle pipeline) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pipeline);
		wgpuRenderPassEncoderSetPipeline(passEnc, pipeline.WgpuRenderPipeline);
		pipelineSet = true;
	}

	/// <summary>
	/// Sets the bind group at <paramref name="index"/>, for a bind group without dynamic offsets.
	/// </summary>
	/// <param name="index">Bind group index, i.e. <c>@group(index)</c> in WGSL.</param>
	/// <param name="bindGroup">Bind group to set.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="bindGroup"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="bindGroup"/>'s layout has bindings with dynamic offsets.
	/// </exception>
	public void SetBindGroup(uint index, GpuBindGroupHandle bindGroup) => SetBindGroup(index, bindGroup, []);

	/// <summary>
	/// Sets the bind group at <paramref name="index"/>, with offsets for its dynamic-offset buffer
	/// bindings.
	/// </summary>
	/// <param name="index">Bind group index, i.e. <c>@group(index)</c> in WGSL.</param>
	/// <param name="bindGroup">Bind group to set.</param>
	/// <param name="dynamicOffsets">
	/// One byte offset per binding declared with
	/// <see cref="GpuBufferBindingLayout.HasDynamicOffset"/>,
	/// in binding order. Each must be a multiple of
	/// <see cref="GpuLimits.MinUniformBufferOffsetAlignment"/>
	/// (uniform buffers) or <see cref="GpuLimits.MinStorageBufferOffsetAlignment"/> (storage buffers).
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="bindGroup"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if the number of <paramref name="dynamicOffsets"/> doesn't match the number of
	/// dynamic-offset bindings in <paramref name="bindGroup"/>'s layout, or an offset isn't aligned
	/// as described above.
	/// </exception>
	public void SetBindGroup(uint index, GpuBindGroupHandle bindGroup, ReadOnlySpan<uint> dynamicOffsets) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(bindGroup);
		BufferBindingType[] dynamic = bindGroup.DynamicBindings;
		if (dynamicOffsets.Length != dynamic.Length)
			throw new ArgumentException($"bind group has {dynamic.Length} dynamic-offset binding(s), but {dynamicOffsets.Length} offset(s) were given", nameof(dynamicOffsets));
		for (int i = 0; i < dynamic.Length; i++) {
			uint align = dynamic[i] == BufferBindingType.Uniform
				? limits.MinUniformBufferOffsetAlignment
				: limits.MinStorageBufferOffsetAlignment;
			if (dynamicOffsets[i] % align != 0)
				throw new ArgumentException($"dynamic offset {i} must be a multiple of {align}", nameof(dynamicOffsets));
		}
		fixed (uint* p = dynamicOffsets)
			wgpuRenderPassEncoderSetBindGroup(passEnc, index, bindGroup.WgpuBindGroup, (nuint)dynamicOffsets.Length, p);
	}

	/// <summary>
	/// Sets the vertex buffer for <paramref name="slot"/>, i.e. the pipeline's
	/// <see cref="VertexState.Buffers"/> entry at that index.
	/// </summary>
	/// <param name="slot">Vertex buffer slot.</param>
	/// <param name="buffer">Buffer to use; must have <see cref="BufferUsage.Vertex"/>.</param>
	/// <param name="offset">Byte offset of the vertex data.</param>
	/// <param name="size">
	/// Size of the vertex data in bytes; the default of <see cref="ulong.MaxValue"/> means up to the
	/// end of the buffer.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="buffer"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="buffer"/> lacks <see cref="BufferUsage.Vertex"/>,
	/// <paramref name="offset"/> isn't a multiple of 4, or the range is out of bounds.
	/// </exception>
	public void SetVertexBuffer(uint slot, GpuBufferHandle buffer, ulong offset = 0, ulong size = wholeSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(buffer);
		requireBuffer(buffer, BufferUsage.Vertex, offset, size, 4);
		wgpuRenderPassEncoderSetVertexBuffer(passEnc, slot, buffer.WgpuBuffer, offset, size);
	}

	/// <summary>
	/// Sets the index buffer used by indexed draws.
	/// </summary>
	/// <param name="buffer">Buffer to use; must have <see cref="BufferUsage.Index"/>.</param>
	/// <param name="format">Element type of the indices.</param>
	/// <param name="offset">Byte offset of the index data.</param>
	/// <param name="size">
	/// Size of the index data in bytes; the default of <see cref="ulong.MaxValue"/> means up to the
	/// end of the buffer.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="buffer"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="buffer"/> lacks <see cref="BufferUsage.Index"/>,
	/// <paramref name="format"/> is <see cref="IndexFormat.Undefined"/>, <paramref name="offset"/>
	/// isn't a multiple of the index size, or the range is out of bounds.
	/// </exception>
	public void SetIndexBuffer(GpuBufferHandle buffer, IndexFormat format, ulong offset = 0, ulong size = wholeSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(buffer);
		if (format == IndexFormat.Undefined)
			throw new ArgumentException("index format must not be Undefined", nameof(format));
		requireBuffer(buffer, BufferUsage.Index, offset, size, format == IndexFormat.Uint16 ? 2u : 4u);
		indexBufferSet = true;
		wgpuRenderPassEncoderSetIndexBuffer(passEnc, buffer.WgpuBuffer, format.ToWebgpuType(), offset, size);
	}

	/// <summary>
	/// Sets the scissor rect to a rectangle, in pixels.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if the rectangle doesn't lie within the pass's attachments.
	/// </exception>
	/// <remarks>
	/// Defaults to the whole attachment at the start of the pass.
	/// </remarks>
	public void SetScissorRect(uint x, uint y, uint width, uint height) {
		ObjectDisposedException.ThrowIf(disposed, this);
		if ((ulong)x + width > this.width || (ulong)y + height > this.height)
			throw new ArgumentException($"scissor rectangle must lie within the {this.width}x{this.height} attachments");
		wgpuRenderPassEncoderSetScissorRect(passEnc, x, y, width, height);
	}

	/// <summary>
	/// Sets the viewport that clip space is mapped to, in pixels, and the depth range.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="width"/> or <paramref name="height"/> is negative or larger than
	/// <see cref="GpuLimits.MaxTextureDimension2d"/>, the viewport extends past the range described
	/// below, or the depth range isn't 0 &lt;= <paramref name="minDepth"/> &lt;=
	/// <paramref name="maxDepth"/> &lt;= 1. Non-finite values are rejected too.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Defaults to the whole attachment and a depth range of [0, 1] at the start of the pass.
	/// </para>
	/// <para>
	/// Following WebGPU, the viewport may extend past the attachments, but must lie within
	/// [-2m, 2m - 1] on both axes, where m is <see cref="GpuLimits.MaxTextureDimension2d"/>.
	/// </para>
	/// </remarks>
	public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f) {
		ObjectDisposedException.ThrowIf(disposed, this);
		// https://www.w3.org/TR/webgpu/#dom-gpurenderpassencoder-setviewport
		float maxDim = limits.MaxTextureDimension2d;
		float range = 2f * maxDim;
		if (!(width >= 0f && width <= maxDim && height >= 0f && height <= maxDim))
			throw new ArgumentException($"viewport size must be in [0, {maxDim}]");
		if (!(x >= -range && y >= -range && x + width <= range - 1f && y + height <= range - 1f))
			throw new ArgumentException($"viewport must lie within [{-range}, {range - 1f}] on both axes");
		if (!(minDepth >= 0f && minDepth <= 1f && maxDepth >= 0f && maxDepth <= 1f && minDepth <= maxDepth))
			throw new ArgumentException("viewport depth range must satisfy 0 <= minDepth <= maxDepth <= 1");
		wgpuRenderPassEncoderSetViewport(passEnc, x, y, width, height, minDepth, maxDepth);
	}

	/// <summary>
	/// Sets the constant color used by <see cref="BlendFactor.Constant"/> and
	/// <see cref="BlendFactor.OneMinusConstant"/>.
	/// </summary>
	/// <remarks>
	/// Defaults to transparent black at the start of the pass.
	/// </remarks>
	public void SetBlendConstant(Vector4 rgba) {
		ObjectDisposedException.ThrowIf(disposed, this);
		WGPUColor c = new(rgba.X, rgba.Y, rgba.Z, rgba.W);
		wgpuRenderPassEncoderSetBlendConstant(passEnc, &c);
	}

	/// <summary>
	/// Sets the reference value for stencil tests and <see cref="StencilOperation.Replace"/>.
	/// </summary>
	/// <remarks>
	/// Defaults to 0 at the start of the pass.
	/// </remarks>
	public void SetStencilReference(uint reference) {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderSetStencilReference(passEnc, reference);
	}

	/// <summary>
	/// Draws non-indexed primitives.
	/// </summary>
	/// <param name="vertexCount">Number of vertices to draw.</param>
	/// <param name="instanceCount">Number of instances to draw.</param>
	/// <param name="firstVertex">First vertex, i.e. the first <c>@builtin(vertex_index)</c>.</param>
	/// <param name="firstInstance">
	/// First instance, i.e. the first <c>@builtin(instance_index)</c>.
	/// </param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if no pipeline has been set with <see cref="SetPipeline(GpuRenderPipelineHandle)"/>.
	/// </exception>
	public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) {
		ObjectDisposedException.ThrowIf(disposed, this);
		chkPipeline();
		wgpuRenderPassEncoderDraw(passEnc, vertexCount, instanceCount, firstVertex, firstInstance);
	}

	/// <summary>
	/// Draws indexed primitives, using the buffer set with
	/// <see cref="SetIndexBuffer(GpuBufferHandle, IndexFormat, ulong, ulong)"/>.
	/// </summary>
	/// <param name="indexCount">Number of indices to draw.</param>
	/// <param name="instanceCount">Number of instances to draw.</param>
	/// <param name="firstIndex">First index to read from the index buffer.</param>
	/// <param name="baseVertex">Value added to each index before reading the vertex.</param>
	/// <param name="firstInstance">
	/// First instance, i.e. the first <c>@builtin(instance_index)</c>.
	/// </param>
	/// <exception cref="InvalidOperationException">
	/// Thrown if no pipeline or no index buffer has been set.
	/// </exception>
	public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) {
		ObjectDisposedException.ThrowIf(disposed, this);
		chkPipeline();
		chkIndexBuffer();
		wgpuRenderPassEncoderDrawIndexed(passEnc, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
	}

	/// <summary>
	/// Draws with arguments read from <paramref name="buffer"/> at <paramref name="offset"/>, laid
	/// out as four <see langword="uint"/>s: vertex count, instance count, first vertex, first
	/// instance.
	/// </summary>
	/// <param name="buffer">
	/// Buffer holding the arguments; must have <see cref="BufferUsage.Indirect"/>.
	/// </param>
	/// <param name="offset">Byte offset of the arguments; must be a multiple of 4.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="buffer"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="buffer"/> lacks <see cref="BufferUsage.Indirect"/>,
	/// <paramref name="offset"/> isn't a multiple of 4, or the arguments don't fit in
	/// <paramref name="buffer"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if no pipeline has been set.
	/// </exception>
	public void DrawIndirect(GpuBufferHandle buffer, ulong offset) {
		chkIndirect(buffer, offset, 16);
		wgpuRenderPassEncoderDrawIndirect(passEnc, buffer.WgpuBuffer, offset);
	}

	/// <summary>
	/// Draws indexed with arguments read from <paramref name="buffer"/> at
	/// <paramref name="offset"/>, laid out as index count, instance count, first index
	/// (<see langword="uint"/>s), base vertex (<see langword="int"/>), first instance
	/// (<see langword="uint"/>).
	/// </summary>
	/// <inheritdoc cref="DrawIndirect(GpuBufferHandle, ulong)" path="/param"/>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="buffer"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="buffer"/> lacks <see cref="BufferUsage.Indirect"/>,
	/// <paramref name="offset"/> isn't a multiple of 4, or the arguments don't fit in
	/// <paramref name="buffer"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if no pipeline or no index buffer has been set.
	/// </exception>
	public void DrawIndexedIndirect(GpuBufferHandle buffer, ulong offset) {
		chkIndirect(buffer, offset, 20);
		chkIndexBuffer();
		wgpuRenderPassEncoderDrawIndexedIndirect(passEnc, buffer.WgpuBuffer, offset);
	}

	private void chkIndirect(GpuBufferHandle buffer, ulong offset, ulong argsSize) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(buffer);
		if (buffer.Usage.HasNone(BufferUsage.Indirect))
			throw new ArgumentException("buffer must have Indirect set in its usages", nameof(buffer));
		if (offset % 4 != 0)
			throw new ArgumentException("must be a multiple of 4", nameof(offset));
		if (offset > buffer.Size || argsSize > buffer.Size - offset)
			throw new ArgumentException($"the {argsSize} bytes of arguments don't fit in the buffer at this offset", nameof(offset));
		chkPipeline();
	}

	private void chkPipeline() {
		if (!pipelineSet)
			throw new InvalidOperationException("no pipeline has been set on this render pass");
	}

	private void chkIndexBuffer() {
		if (!indexBufferSet)
			throw new InvalidOperationException("no index buffer has been set on this render pass");
	}

	private static void requireBuffer(GpuBufferHandle buffer, BufferUsage usage, ulong offset, ulong size, uint alignment) {
		if (buffer.Usage.HasNone(usage))
			throw new ArgumentException($"buffer must have {usage} set in its usages", nameof(buffer));
		if (offset % alignment != 0)
			throw new ArgumentException($"must be a multiple of {alignment}", nameof(offset));
		if (offset > buffer.Size || (size != wholeSize && size > buffer.Size - offset))
			throw new ArgumentException("range is out of the buffer's bounds", nameof(offset));
	}

	/// <summary>
	/// Starts a labeled group of commands, for debugging tools such as RenderDoc. Must be closed
	/// with <see cref="PopDebugGroup()"/> before the pass ends.
	/// </summary>
	public void PushDebugGroup(string label) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(label);
		wgpuRenderPassEncoderPushDebugGroup(passEnc, label);
	}

	/// <summary>
	/// Ends the group started by the last <see cref="PushDebugGroup(string)"/>.
	/// </summary>
	public void PopDebugGroup() {
		ObjectDisposedException.ThrowIf(disposed, this);
		wgpuRenderPassEncoderPopDebugGroup(passEnc);
	}

	/// <summary>
	/// Inserts a labeled marker, for debugging tools such as RenderDoc.
	/// </summary>
	public void InsertDebugMarker(string label) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(label);
		wgpuRenderPassEncoderInsertDebugMarker(passEnc, label);
	}

	/// <summary>
	/// Ends the pass, letting its encoder record other commands again.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		wgpuRenderPassEncoderEnd(passEnc);
		wgpuRenderPassEncoderRelease(passEnc);
		onFinished?.Invoke();
	}
}
