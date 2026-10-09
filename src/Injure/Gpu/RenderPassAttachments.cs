// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// A color attachment of a render pass, see
/// <see cref="GpuCommandEncoderHandle.BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)"/>.
/// </summary>
/// <param name="View">View to render into; must have a color format.</param>
/// <param name="Ops">Load/store operations for <paramref name="View"/>.</param>
/// <param name="ResolveTarget">
/// If not <see langword="null"/>, the view that the multisampled <paramref name="View"/> is
/// resolved into at the end of the pass. Must be single-sampled and have the same format and size
/// as <paramref name="View"/>, which must be multisampled.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct RenderPassColorAttachment(
	GpuTextureViewHandle View,
	ColorAttachmentOps Ops,
	GpuTextureViewHandle? ResolveTarget = null
);

/// <summary>
/// The depth/stencil attachment of a render pass, see
/// <see cref="GpuCommandEncoderHandle.BeginRenderPass(ReadOnlySpan{RenderPassColorAttachment}, in RenderPassDepthStencilAttachment?)"/>.
/// </summary>
/// <param name="View">View to use; must have a depth and/or stencil format.</param>
/// <param name="DepthOps">
/// Load/store operations for the depth aspect; <see langword="null"/> makes depth read-only for
/// the pass. Must be <see langword="null"/> if the format has no depth.
/// </param>
/// <param name="StencilOps">
/// Load/store operations for the stencil aspect; <see langword="null"/> makes stencil read-only
/// for the pass. Must be <see langword="null"/> if the format has no stencil.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct RenderPassDepthStencilAttachment(
	GpuTextureViewHandle View,
	DepthAttachmentOps? DepthOps = null,
	StencilAttachmentOps? StencilOps = null
);
