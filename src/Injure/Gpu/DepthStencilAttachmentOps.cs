// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// What a render pass does with the depth aspect of a depth/stencil attachment at its start and
/// end.
/// </summary>
/// <param name="LoadOp">What to do with the existing depth values at the start of the pass.</param>
/// <param name="StoreOp">What to do with the rendered depth values at the end of the pass.</param>
/// <param name="ClearValue">
/// Depth to clear to if <paramref name="LoadOp"/> is <see cref="LoadOp.Clear"/>; must be in [0, 1].
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since writable depth can't use
/// <see cref="LoadOp.Undefined"/>/<see cref="StoreOp.Undefined"/>. For read-only depth, pass no
/// <see cref="DepthAttachmentOps"/> at all; see <see cref="RenderPassDepthStencilAttachment"/>.
/// </remarks>
public readonly record struct DepthAttachmentOps(
	LoadOp LoadOp,
	StoreOp StoreOp,
	float ClearValue
) {
	/// <summary>
	/// Keeps the existing depth values and stores the rendered ones.
	/// </summary>
	public static readonly DepthAttachmentOps Load = new(
		LoadOp: LoadOp.Load,
		StoreOp: StoreOp.Store,
		ClearValue: 1f
	);

	/// <summary>
	/// Clears to <paramref name="value"/> and stores the rendered depth values.
	/// </summary>
	public static DepthAttachmentOps Clear(float value) => new(
		LoadOp: LoadOp.Clear,
		StoreOp: StoreOp.Store,
		ClearValue: value
	);
}

/// <summary>
/// What a render pass does with the stencil aspect of a depth/stencil attachment at its start and
/// end.
/// </summary>
/// <param name="LoadOp">
/// What to do with the existing stencil values at the start of the pass.
/// </param>
/// <param name="StoreOp">
/// What to do with the rendered stencil values at the end of the pass.
/// </param>
/// <param name="ClearValue">
/// Stencil value to clear to if <paramref name="LoadOp"/> is <see cref="LoadOp.Clear"/>.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since writable stencil can't use
/// <see cref="LoadOp.Undefined"/>/<see cref="StoreOp.Undefined"/>. For read-only stencil, pass no
/// <see cref="StencilAttachmentOps"/> at all; see <see cref="RenderPassDepthStencilAttachment"/>.
/// </remarks>
public readonly record struct StencilAttachmentOps(
	LoadOp LoadOp,
	StoreOp StoreOp,
	uint ClearValue
) {
	/// <summary>
	/// Keeps the existing stencil values and stores the rendered ones.
	/// </summary>
	public static readonly StencilAttachmentOps Load = new(
		LoadOp: LoadOp.Load,
		StoreOp: StoreOp.Store,
		ClearValue: 0
	);

	/// <summary>
	/// Clears to <paramref name="value"/> and stores the rendered stencil values.
	/// </summary>
	public static StencilAttachmentOps Clear(uint value) => new(
		LoadOp: LoadOp.Clear,
		StoreOp: StoreOp.Store,
		ClearValue: value
	);
}
