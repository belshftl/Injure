// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using WebGPU;
using static WebGPU.WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Common base type for command encoder wrappers, allowing APIs to accept both owning and
/// non-owning wrappers. Holds the command recording methods.
/// </summary>
/// <remarks>
/// <para>
/// Commands run when the buffer returned by <see cref="GpuCommandEncoder.Finish()"/> is passed to
/// <see cref="GpuDevice.Submit(GpuCommandBuffer)"/>, in the order they were recorded.
/// </para>
/// <para>
/// Only one <see cref="RenderPass"/> may be active at a time, and no other command can be recorded
/// while one is. Once the owning encoder is finished or disposed, recording through any wrapper
/// throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Not thread-safe.
/// </para>
/// </remarks>
public abstract unsafe class GpuCommandEncoderHandle {
	private const ulong wholeSize = ulong.MaxValue;
	private const uint copyBytesPerRowAlignment = 256;

	// shared by an owning encoder and all of its refs
	internal sealed class EncoderState(WGPUCommandEncoder encoder, GpuLimits limits) {
		public WGPUCommandEncoder Encoder = encoder;
		public readonly GpuLimits Limits = limits;
		public bool ActivePass = false;
		public bool Done = false;
	}

	internal abstract EncoderState State { get; }

	private protected GpuCommandEncoderHandle() {
	}

	private protected void chkRecording() {
		if (State.Done)
			throw new InvalidOperationException("encoder already finished/disposed");
		if (State.ActivePass)
			throw new InvalidOperationException("encoder has an active render pass");
	}

	private void onPassFinished() {
		if (!State.ActivePass)
			throw new InternalStateException("onPassFinished called but no pass is currently active");
		State.ActivePass = false;
	}

	/// <summary>
	/// Opens a render pass.
	/// </summary>
	/// <param name="colorAttachments">
	/// Color attachments, in the order of the pipeline's <see cref="FragmentState.Targets"/>. May
	/// be empty if <paramref name="depthStencil"/> isn't <see langword="null"/>.
	/// </param>
	/// <param name="depthStencil">The depth/stencil attachment, if any.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if an attachment's view is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if there are no attachments, more color attachments than
	/// <see cref="GpuLimits.MaxColorAttachments"/>, or an attachment is invalid as described in
	/// the remarks and on <see cref="RenderPassColorAttachment"/> and
	/// <see cref="RenderPassDepthStencilAttachment"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the encoder has already been finished or disposed, or if a render pass is
	/// already active.
	/// </exception>
	/// <remarks>
	/// <para>
	/// All attachments must be 2D views with <see cref="TextureUsage.RenderAttachment"/>, and have
	/// the same size and sample count.
	/// </para>
	/// <para>
	/// The returned <see cref="RenderPass"/> object must be disposed before any other command
	/// can be recorded, and before the encoder can be finished.
	/// </para>
	/// </remarks>
	public RenderPass BeginRenderPass(
		ReadOnlySpan<RenderPassColorAttachment> colorAttachments,
		in RenderPassDepthStencilAttachment? depthStencil = null
	) {
		chkRecording();
		if (colorAttachments.IsEmpty && depthStencil is null)
			throw new ArgumentException("a render pass needs at least one attachment", nameof(colorAttachments));
		if (colorAttachments.Length > State.Limits.MaxColorAttachments)
			throw new ArgumentException($"at most {State.Limits.MaxColorAttachments} color attachments are supported", nameof(colorAttachments));

		GpuTextureViewHandle? first = null;
		foreach (RenderPassColorAttachment ca in colorAttachments) {
			validateColorView(ca.View, nameof(colorAttachments));
			if (first is null)
				first = ca.View;
			else
				validateCompatibleAttachments(first, ca.View, nameof(colorAttachments));
			if (ca.ResolveTarget is GpuTextureViewHandle rt)
				validateResolveTarget(ca.View, rt, nameof(colorAttachments));
		}
		if (depthStencil is RenderPassDepthStencilAttachment ds) {
			validateDepthStencilAttachment(ds, nameof(depthStencil));
			if (first is not null)
				validateCompatibleAttachments(first, ds.View, nameof(depthStencil));
		}

		WGPURenderPassColorAttachment* colors = stackalloc WGPURenderPassColorAttachment[colorAttachments.Length];
		for (int i = 0; i < colorAttachments.Length; i++) {
			ref readonly RenderPassColorAttachment ca = ref colorAttachments[i];
			colors[i] = new WGPURenderPassColorAttachment {
				view = ca.View.WgpuTextureView,
				resolveTarget = ca.ResolveTarget?.WgpuTextureView ?? default,
				loadOp = ca.Ops.LoadOp.ToWebgpuType(),
				storeOp = ca.Ops.StoreOp.ToWebgpuType(),
				clearValue = ca.Ops.ClearValue.ToWebgpuColor(),
				depthSlice = WGPU_DEPTH_SLICE_UNDEFINED,
			};
		}
		WGPURenderPassDescriptor desc = new() {
			colorAttachmentCount = (nuint)colorAttachments.Length,
			colorAttachments = colors,
		};

		WGPURenderPassDepthStencilAttachment dsRaw;
		if (depthStencil is RenderPassDepthStencilAttachment d) {
			dsRaw = new WGPURenderPassDepthStencilAttachment {
				view = d.View.WgpuTextureView,
				depthReadOnly = d.DepthOps is null,
				stencilReadOnly = d.StencilOps is null,
			};
			if (d.DepthOps is DepthAttachmentOps dop) {
				dsRaw.depthLoadOp = dop.LoadOp.ToWebgpuType();
				dsRaw.depthStoreOp = dop.StoreOp.ToWebgpuType();
				dsRaw.depthClearValue = dop.ClearValue;
			}
			if (d.StencilOps is StencilAttachmentOps sop) {
				dsRaw.stencilLoadOp = sop.LoadOp.ToWebgpuType();
				dsRaw.stencilStoreOp = sop.StoreOp.ToWebgpuType();
				dsRaw.stencilClearValue = sop.ClearValue;
			}
			desc.depthStencilAttachment = &dsRaw;
		}

		WGPURenderPassEncoder passEnc = WebgpuException.Check(wgpuCommandEncoderBeginRenderPass(State.Encoder, &desc));
		State.ActivePass = true;
		GpuTextureViewHandle sizeSource = first ?? depthStencil!.Value.View;
		return new RenderPass(passEnc, onPassFinished, State.Limits, sizeSource.Width, sizeSource.Height);
	}

	[StackTraceHidden]
	private static void validateView(GpuTextureViewHandle view, string paramName) {
		if (view is null)
			throw new ArgumentNullException(paramName, "attachment view is null");
		if (view.Usage.HasNone(TextureUsage.RenderAttachment))
			throw new ArgumentException("attachment views must have RenderAttachment set in their usages", paramName);
		if (view.Dimension != TextureViewDimension.Dimension2d)
			throw new ArgumentException("attachment views must be 2D", paramName);
	}

	private static bool hasDepth(TextureFormat f) => f.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus
		or TextureFormat.Case.Depth32Float or TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8;

	private static bool hasStencil(TextureFormat f) => f.Tag is TextureFormat.Case.Stencil8
		or TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8;

	[StackTraceHidden]
	private static void validateColorView(GpuTextureViewHandle view, string paramName) {
		validateView(view, paramName);
		if (hasDepth(view.Format) || hasStencil(view.Format))
			throw new ArgumentException("color attachment views must have a color format", paramName);
	}

	[StackTraceHidden]
	private static void validateResolveTarget(GpuTextureViewHandle view, GpuTextureViewHandle target, string paramName) {
		validateView(target, paramName);
		if (view.SampleCount == 1)
			throw new ArgumentException("only multisampled color attachments can have a resolve target", paramName);
		if (target.SampleCount != 1)
			throw new ArgumentException("resolve targets must not be multisampled", paramName);
		if (target.Format != view.Format)
			throw new ArgumentException("resolve targets must have the same format as their attachment", paramName);
		if (target.Width != view.Width || target.Height != view.Height)
			throw new ArgumentException("resolve targets must have the same size as their attachment", paramName);
	}

	[StackTraceHidden]
	private static void validateDepthStencilAttachment(in RenderPassDepthStencilAttachment ds, string paramName) {
		validateView(ds.View, paramName);
		TextureFormat f = ds.View.Format;
		if (!hasDepth(f) && !hasStencil(f))
			throw new ArgumentException("depth/stencil attachment views must have a depth and/or stencil format", paramName);
		if (ds.DepthOps is not null && !hasDepth(f))
			throw new ArgumentException("DepthOps given for a format without depth", paramName);
		if (ds.StencilOps is not null && !hasStencil(f))
			throw new ArgumentException("StencilOps given for a format without stencil", paramName);
	}

	[StackTraceHidden]
	private static void validateCompatibleAttachments(GpuTextureViewHandle a, GpuTextureViewHandle b, string paramName) {
		if (a.Width != b.Width || a.Height != b.Height)
			throw new ArgumentException("attachment views must have equal dimensions", paramName);
		if (a.SampleCount != b.SampleCount)
			throw new ArgumentException("attachment views must have equal sample counts", paramName);
	}

	/// <summary>
	/// Opens a render pass targeting a single color attachment; shorthand for
	/// <see cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)"/>.
	/// </summary>
	/// <param name="colorView">Color texture view to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <inheritdoc cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)" path="/exception"/>
	/// <inheritdoc cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)" path="/remarks"/>
	public RenderPass BeginColorPass(GpuTextureViewHandle colorView, in ColorAttachmentOps colorOps) =>
		BeginRenderPass([new RenderPassColorAttachment(colorView, colorOps)]);

	/// <summary>
	/// Opens a render pass targeting a color attachment and a depth-only attachment; shorthand for
	/// <see cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)"/>.
	/// </summary>
	/// <param name="colorView">Color texture view to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <param name="depthView">Depth texture view to use; must have a depth-only format.</param>
	/// <param name="depthOps">Depth attachment load/store operations.</param>
	/// <inheritdoc cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)" path="/exception"/>
	/// <inheritdoc cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)" path="/remarks"/>
	public RenderPass BeginColorDepthPass(
		GpuTextureViewHandle colorView,
		in ColorAttachmentOps colorOps,
		GpuTextureViewHandle depthView,
		in DepthAttachmentOps depthOps
	) {
		ArgumentNullException.ThrowIfNull(depthView);
		if (hasStencil(depthView.Format))
			throw new ArgumentException("depth view must have a depth-only format", nameof(depthView));
		return BeginRenderPass(
			[new RenderPassColorAttachment(colorView, colorOps)],
			new RenderPassDepthStencilAttachment(depthView, depthOps)
		);
	}

	/// <summary>
	/// Opens a render pass targeting a color attachment and a depth+stencil attachment; shorthand for
	/// <see cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)"/>.
	/// </summary>
	/// <param name="colorView">Color texture view to use.</param>
	/// <param name="colorOps">Color attachment load/store operations.</param>
	/// <param name="depthStencilView">
	/// Depth+stencil texture view to use; must have a depth+stencil format.
	/// </param>
	/// <param name="depthOps">Depth attachment load/store operations.</param>
	/// <param name="stencilOps">Stencil attachment load/store operations.</param>
	/// <inheritdoc cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)" path="/exception"/>
	/// <inheritdoc cref="BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)" path="/remarks"/>
	public RenderPass BeginColorDepthStencilPass(
		GpuTextureViewHandle colorView,
		in ColorAttachmentOps colorOps,
		GpuTextureViewHandle depthStencilView,
		in DepthAttachmentOps depthOps,
		in StencilAttachmentOps stencilOps
	) {
		ArgumentNullException.ThrowIfNull(depthStencilView);
		if (!hasDepth(depthStencilView.Format) || !hasStencil(depthStencilView.Format))
			throw new ArgumentException("depth+stencil view must have a depth+stencil format", nameof(depthStencilView));
		return BeginRenderPass(
			[new RenderPassColorAttachment(colorView, colorOps)],
			new RenderPassDepthStencilAttachment(depthStencilView, depthOps, stencilOps)
		);
	}

	/// <summary>
	/// Starts a labelled group of commands, for debugging tools such as RenderDoc. Must be closed
	/// with <see cref="PopDebugGroup()"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the encoder has already been finished or disposed, or if a render pass is active.
	/// </exception>
	public void PushDebugGroup(string label) {
		ArgumentNullException.ThrowIfNull(label);
		chkRecording();
		wgpuCommandEncoderPushDebugGroup(State.Encoder, label);
	}

	/// <summary>
	/// Ends the group started by the last <see cref="PushDebugGroup(string)"/>.
	/// </summary>
	/// <inheritdoc cref="PushDebugGroup(string)" path="/exception"/>
	public void PopDebugGroup() {
		chkRecording();
		wgpuCommandEncoderPopDebugGroup(State.Encoder);
	}

	/// <summary>
	/// Inserts a labelled marker, for debugging tools such as RenderDoc.
	/// </summary>
	/// <inheritdoc cref="PushDebugGroup(string)" path="/exception"/>
	public void InsertDebugMarker(string label) {
		ArgumentNullException.ThrowIfNull(label);
		chkRecording();
		wgpuCommandEncoderInsertDebugMarker(State.Encoder, label);
	}

	/// <summary>
	/// Copies <paramref name="size"/> bytes from one buffer to another.
	/// </summary>
	/// <param name="src">Source buffer; must have <see cref="BufferUsage.CopySrc"/>.</param>
	/// <param name="srcOffset">
	/// Byte offset into <paramref name="src"/>; must be a multiple of 4.
	/// </param>
	/// <param name="dst">Destination buffer; must have <see cref="BufferUsage.CopyDst"/>.</param>
	/// <param name="dstOffset">
	/// Byte offset into <paramref name="dst"/>; must be a multiple of 4.
	/// </param>
	/// <param name="size">Number of bytes to copy; must be a multiple of 4.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="src"/> or <paramref name="dst"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if a buffer lacks the required usage, <paramref name="src"/> and
	/// <paramref name="dst"/> are the same buffer, an offset or the size isn't a multiple of 4, or
	/// a range is out of bounds.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the encoder has already been finished or disposed, or if a render pass is active.
	/// </exception>
	public void CopyBufferToBuffer(GpuBufferHandle src, ulong srcOffset, GpuBufferHandle dst, ulong dstOffset, ulong size) {
		ArgumentNullException.ThrowIfNull(src);
		ArgumentNullException.ThrowIfNull(dst);
		chkRecording();
		requireBufferUsage(src, BufferUsage.CopySrc, nameof(src));
		requireBufferUsage(dst, BufferUsage.CopyDst, nameof(dst));
		if (src.WgpuBuffer == dst.WgpuBuffer)
			throw new ArgumentException("source and destination must be different buffers", nameof(dst));
		requireMultipleOf4(srcOffset, nameof(srcOffset));
		requireMultipleOf4(dstOffset, nameof(dstOffset));
		requireMultipleOf4(size, nameof(size));
		requireInBounds(src, srcOffset, size, nameof(srcOffset));
		requireInBounds(dst, dstOffset, size, nameof(dstOffset));
		wgpuCommandEncoderCopyBufferToBuffer(State.Encoder, src.WgpuBuffer, srcOffset, dst.WgpuBuffer, dstOffset, size);
	}

	/// <summary>
	/// Copies texel data from a buffer into a texture region.
	/// </summary>
	/// <param name="src">Source buffer; must have <see cref="BufferUsage.CopySrc"/>.</param>
	/// <param name="srcLayout">
	/// Layout of the data in <paramref name="src"/>. Unlike for
	/// <see cref="GpuDevice.WriteToTexture(GpuTextureHandle, in GpuTextureRegion, void*, nuint, in GpuTextureLayout)"/>,
	/// <see cref="GpuTextureLayout.BytesPerRow"/> must be a multiple of 256.
	/// </param>
	/// <param name="dst">Destination texture; must have <see cref="TextureUsage.CopyDst"/>.</param>
	/// <param name="dstRegion">Destination texture region and subresource.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="src"/> or <paramref name="dst"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="src"/> or <paramref name="dst"/> lacks the required usage,
	/// <see cref="GpuTextureLayout.BytesPerRow"/> isn't a multiple of 256, or the texture region
	/// names a mip level that doesn't exist or is out of its bounds.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the encoder has already been finished or disposed, or if a render pass is active.
	/// </exception>
	public void CopyBufferToTexture(GpuBufferHandle src, in GpuTextureLayout srcLayout, GpuTextureHandle dst, in GpuTextureRegion dstRegion) {
		ArgumentNullException.ThrowIfNull(src);
		ArgumentNullException.ThrowIfNull(dst);
		chkRecording();
		requireBufferUsage(src, BufferUsage.CopySrc, nameof(src));
		requireTextureUsage(dst, TextureUsage.CopyDst, nameof(dst));
		requireCopyLayout(srcLayout, nameof(srcLayout));
		dst.RequireRegionInBounds(dstRegion, nameof(dstRegion));
		WGPUTexelCopyBufferInfo b = toBufferInfo(src, srcLayout);
		WGPUTexelCopyTextureInfo t = toTextureInfo(dst, dstRegion);
		WGPUExtent3D extent = toExtent(dstRegion);
		wgpuCommandEncoderCopyBufferToTexture(State.Encoder, &b, &t, &extent);
	}

	/// <summary>
	/// Copies texel data from a texture region into a buffer.
	/// </summary>
	/// <param name="src">Source texture; must have <see cref="TextureUsage.CopySrc"/>.</param>
	/// <param name="srcRegion">Source texture region and subresource.</param>
	/// <param name="dst">Destination buffer; must have <see cref="BufferUsage.CopyDst"/>.</param>
	/// <param name="dstLayout">
	/// Layout to write the data in; <see cref="GpuTextureLayout.BytesPerRow"/> must be a multiple
	/// of 256.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="src"/> or <paramref name="dst"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="src"/> or <paramref name="dst"/> lacks the required usage,
	/// <see cref="GpuTextureLayout.BytesPerRow"/> isn't a multiple of 256, or the texture region
	/// names a mip level that doesn't exist or is out of its bounds.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the encoder has already been finished or disposed, or if a render pass is active.
	/// </exception>
	/// <remarks>
	/// This is the first half of reading a texture back to the CPU; the second half is mapping
	/// <paramref name="dst"/> once the command buffer has been submitted, see
	/// <see cref="GpuBuffer.BeginMap(MapMode, ulong, ulong?, Action{BufferMapStatus})"/>.
	/// </remarks>
	public void CopyTextureToBuffer(GpuTextureHandle src, in GpuTextureRegion srcRegion, GpuBufferHandle dst, in GpuTextureLayout dstLayout) {
		ArgumentNullException.ThrowIfNull(src);
		ArgumentNullException.ThrowIfNull(dst);
		chkRecording();
		requireTextureUsage(src, TextureUsage.CopySrc, nameof(src));
		requireBufferUsage(dst, BufferUsage.CopyDst, nameof(dst));
		requireCopyLayout(dstLayout, nameof(dstLayout));
		src.RequireRegionInBounds(srcRegion, nameof(srcRegion));
		WGPUTexelCopyTextureInfo t = toTextureInfo(src, srcRegion);
		WGPUTexelCopyBufferInfo b = toBufferInfo(dst, dstLayout);
		WGPUExtent3D extent = toExtent(srcRegion);
		wgpuCommandEncoderCopyTextureToBuffer(State.Encoder, &t, &b, &extent);
	}

	/// <summary>
	/// Copies a region of one texture into another.
	/// </summary>
	/// <param name="src">Source texture; must have <see cref="TextureUsage.CopySrc"/>.</param>
	/// <param name="srcRegion">Source texture region and subresource.</param>
	/// <param name="dst">Destination texture; must have <see cref="TextureUsage.CopyDst"/>.</param>
	/// <param name="dstRegion">
	/// Destination texture region and subresource; must be the same size as
	/// <paramref name="srcRegion"/>.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="src"/> or <paramref name="dst"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="src"/> or <paramref name="dst"/> lacks the required usage, the
	/// regions differ in size, or a region names a mip level that doesn't exist or is out of its
	/// bounds.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the encoder has already been finished or disposed, or if a render pass is active.
	/// </exception>
	public void CopyTextureToTexture(GpuTextureHandle src, in GpuTextureRegion srcRegion, GpuTextureHandle dst, in GpuTextureRegion dstRegion) {
		ArgumentNullException.ThrowIfNull(src);
		ArgumentNullException.ThrowIfNull(dst);
		chkRecording();
		requireTextureUsage(src, TextureUsage.CopySrc, nameof(src));
		requireTextureUsage(dst, TextureUsage.CopyDst, nameof(dst));
		if (
			srcRegion.Width != dstRegion.Width
			|| srcRegion.Height != dstRegion.Height
			|| srcRegion.DepthOrArrayLayers != dstRegion.DepthOrArrayLayers
		)
			throw new ArgumentException("source and destination regions must be the same size", nameof(dstRegion));
		src.RequireRegionInBounds(srcRegion, nameof(srcRegion));
		dst.RequireRegionInBounds(dstRegion, nameof(dstRegion));
		WGPUTexelCopyTextureInfo s = toTextureInfo(src, srcRegion);
		WGPUTexelCopyTextureInfo d = toTextureInfo(dst, dstRegion);
		WGPUExtent3D extent = toExtent(srcRegion);
		wgpuCommandEncoderCopyTextureToTexture(State.Encoder, &s, &d, &extent);
	}

	/// <summary>
	/// Fills a range of a buffer with zeroes.
	/// </summary>
	/// <param name="buffer">Buffer to clear; must have <see cref="BufferUsage.CopyDst"/>.</param>
	/// <param name="offset">
	/// Byte offset into <paramref name="buffer"/>; must be a multiple of 4.
	/// </param>
	/// <param name="size">
	/// Number of bytes to clear; must be a multiple of 4. <see langword="null"/> clears up to the end
	/// of the buffer.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="buffer"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="buffer"/> lacks <see cref="BufferUsage.CopyDst"/>,
	/// <paramref name="offset"/> or <paramref name="size"/> isn't a multiple of 4, or the range is
	/// out of bounds.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the encoder has already been finished or disposed, or if a render pass is active.
	/// </exception>
	public void ClearBuffer(GpuBufferHandle buffer, ulong offset = 0, ulong? size = null) {
		ArgumentNullException.ThrowIfNull(buffer);
		chkRecording();
		requireBufferUsage(buffer, BufferUsage.CopyDst, nameof(buffer));
		requireMultipleOf4(offset, nameof(offset));
		if (offset > buffer.Size)
			throw new ArgumentException("offset is past the end of the buffer", nameof(offset));
		ulong sz = size ?? buffer.Size - offset;
		requireMultipleOf4(sz, nameof(size));
		requireInBounds(buffer, offset, sz, nameof(size));
		wgpuCommandEncoderClearBuffer(State.Encoder, buffer.WgpuBuffer, offset, sz);
	}

	[StackTraceHidden]
	private static void requireBufferUsage(GpuBufferHandle buffer, BufferUsage usage, string paramName) {
		if (buffer.Usage.HasNone(usage))
			throw new ArgumentException($"buffer must have {usage} set in its usages", paramName);
	}

	[StackTraceHidden]
	private static void requireTextureUsage(GpuTextureHandle tex, TextureUsage usage, string paramName) {
		if (tex.Usage.HasNone(usage))
			throw new ArgumentException($"texture must have {usage} set in its usages", paramName);
	}

	[StackTraceHidden]
	private static void requireMultipleOf4(ulong v, string paramName) {
		if (v % 4 != 0)
			throw new ArgumentException("must be a multiple of 4", paramName);
	}

	[StackTraceHidden]
	private static void requireInBounds(GpuBufferHandle buffer, ulong offset, ulong size, string paramName) {
		if (offset > buffer.Size || size > buffer.Size - offset)
			throw new ArgumentException("range is out of the buffer's bounds", paramName);
	}

	[StackTraceHidden]
	private static void requireCopyLayout(in GpuTextureLayout layout, string paramName) {
		if (layout.BytesPerRow % copyBytesPerRowAlignment != 0)
			throw new ArgumentException($"BytesPerRow must be a multiple of {copyBytesPerRowAlignment} for copies", paramName);
	}

	private static WGPUTexelCopyBufferInfo toBufferInfo(GpuBufferHandle buffer, in GpuTextureLayout layout) => new() {
		buffer = buffer.WgpuBuffer,
		layout = new WGPUTexelCopyBufferLayout {
			offset = layout.Offset,
			bytesPerRow = layout.BytesPerRow,
			rowsPerImage = layout.RowsPerImage,
		},
	};

	private static WGPUTexelCopyTextureInfo toTextureInfo(GpuTextureHandle tex, in GpuTextureRegion region) => new() {
		texture = tex.WgpuTexture,
		mipLevel = region.MipLevel,
		origin = new WGPUOrigin3D { x = region.X, y = region.Y, z = region.Z },
		aspect = region.Aspect.ToWebgpuType(),
	};

	private static WGPUExtent3D toExtent(in GpuTextureRegion region) => new() {
		width = region.Width,
		height = region.Height,
		depthOrArrayLayers = region.DepthOrArrayLayers,
	};
}

/// <summary>
/// Owning command encoder: records GPU commands (render passes, copies, clears) into a
/// <see cref="GpuCommandBuffer"/>.
/// </summary>
/// <remarks>
/// Created by <see cref="GpuDevice.CreateCommandEncoder()"/>. Disposing an encoder without
/// finishing it discards everything recorded so far. See <see cref="GpuCommandEncoderHandle"/> for
/// the recording rules.
/// </remarks>
public sealed unsafe class GpuCommandEncoder : GpuCommandEncoderHandle, IDisposable {
	internal GpuCommandEncoder(WGPUCommandEncoder encoder, GpuLimits limits) {
		State = new EncoderState(encoder, limits);
	}

	internal override EncoderState State { get; }

	/// <summary>
	/// Creates a non-owning view of this encoder, which can record commands but can't finish or
	/// dispose it.
	/// </summary>
	public GpuCommandEncoderRef AsRef() => new(this);

	/// <summary>
	/// Finishes recording, returning the recorded commands.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if the encoder has already been finished or disposed, or if a render pass is active.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Errors in the recorded commands are reported to the device's
	/// <see cref="GpuDeviceOptions.ErrorHandler"/> during this call, and make the returned buffer
	/// invalid; see <see cref="GpuCommandBuffer.IsValid"/>.
	/// </para>
	/// <para>
	/// The encoder can't be used afterwards; disposing it is still allowed and does nothing.
	/// </para>
	/// </remarks>
	public GpuCommandBuffer Finish() {
		chkRecording();
		WGPUCommandBufferDescriptor desc = default;
		ulong errorsBefore = GpuDevice.ErrorsOnCurrentThread;
		WGPUCommandBuffer cmdbuf = WebgpuException.Check(wgpuCommandEncoderFinish(State.Encoder, &desc));
		bool valid = GpuDevice.ErrorsOnCurrentThread == errorsBefore;
		release();
		return new GpuCommandBuffer(cmdbuf, valid);
	}

	/// <summary>
	/// Discards the encoder and everything recorded into it, unless it has already been finished.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if a render pass is still active.
	/// </exception>
	public void Dispose() {
		if (State.Done)
			return;
		if (State.ActivePass)
			throw new InvalidOperationException("encoder still has an active render pass");
		release();
	}

	private void release() {
		State.Done = true;
		wgpuCommandEncoderRelease(State.Encoder);
		State.Encoder = default;
	}
}

/// <summary>
/// Non-owning wrapper around a command encoder; can record commands, but not finish or dispose
/// the encoder.
/// </summary>
public sealed class GpuCommandEncoderRef : GpuCommandEncoderHandle {
	private readonly GpuCommandEncoder source;

	internal GpuCommandEncoderRef(GpuCommandEncoder source) {
		this.source = source;
	}

	internal override EncoderState State => source.State;
}

/// <summary>
/// Commands recorded by a <see cref="GpuCommandEncoder"/>, ready to be submitted with
/// <see cref="GpuDevice.Submit(GpuCommandBuffer)"/>.
/// </summary>
/// <remarks>
/// Can only be submitted once. Disposing a buffer that wasn't submitted discards its commands.
/// </remarks>
public sealed class GpuCommandBuffer : IDisposable {
	internal WGPUCommandBuffer WgpuCommandBuffer { get; private set; }

	internal GpuCommandBuffer(WGPUCommandBuffer cmdbuf, bool valid) {
		WgpuCommandBuffer = cmdbuf;
		IsValid = valid;
	}

	/// <summary>
	/// Whether the commands are valid, i.e. WebGPU reported no error while
	/// <see cref="GpuCommandEncoder.Finish()"/> produced them.
	/// </summary>
	/// <remarks>
	/// An invalid buffer can't be submitted, since WebGPU treats submitting one as fatal; the error
	/// itself went to the device's <see cref="GpuDeviceOptions.ErrorHandler"/>. Dispose it instead.
	/// </remarks>
	public bool IsValid { get; }

	/// <summary>
	/// Whether this buffer has been submitted or disposed.
	/// </summary>
	public bool IsConsumed => WgpuCommandBuffer.IsNull;

	// called by GpuDevice.Submit right after submission, and by Dispose
	internal void Release() {
		if (WgpuCommandBuffer.IsNull)
			return;
		wgpuCommandBufferRelease(WgpuCommandBuffer);
		WgpuCommandBuffer = default;
	}

	/// <summary>
	/// Releases the commands without submitting them, unless they've already been submitted.
	/// </summary>
	public void Dispose() => Release();
}
