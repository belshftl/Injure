// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.Primitives;

namespace Injure.Gpu;

/// <summary>
/// What a render pass does with a color attachment at its start and end.
/// </summary>
/// <param name="LoadOp">What to do with the existing contents at the start of the pass.</param>
/// <param name="StoreOp">What to do with the rendered contents at the end of the pass.</param>
/// <param name="ClearValue">
/// Color to clear to if <paramref name="LoadOp"/> is <see cref="LoadOp.Clear"/>. The value is
/// written the way a fragment shader output would be: unchanged for non-sRGB formats (so it has to
/// be in the attachment's encoding), and encoded from linear for sRGB formats.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since color attachments can't use
/// <see cref="LoadOp.Undefined"/>/<see cref="StoreOp.Undefined"/>.
/// </remarks>
public readonly record struct ColorAttachmentOps(
	LoadOp LoadOp,
	StoreOp StoreOp,
	RawColorF128 ClearValue
) {
	/// <summary>
	/// Keeps the existing contents and stores the rendered ones.
	/// </summary>
	public static readonly ColorAttachmentOps Load = new(LoadOp.Load, StoreOp.Store, default);

	/// <summary>
	/// Clears to <paramref name="color"/> and stores the rendered contents.
	/// </summary>
	public static ColorAttachmentOps Clear(RawColorF128 color) => new(LoadOp.Clear, StoreOp.Store, color);
}
