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
/// Color to clear to if <paramref name="LoadOp"/> is <see cref="LoadOp.Clear"/>.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since color attachments can't use
/// <see cref="LoadOp.Undefined"/>/<see cref="StoreOp.Undefined"/>.
/// </remarks>
public readonly record struct ColorAttachmentOps(
	LoadOp LoadOp,
	StoreOp StoreOp,
	Color32 ClearValue
) {
	/// <summary>
	/// Keeps the existing contents and stores the rendered ones.
	/// </summary>
	public static readonly ColorAttachmentOps Load = new(LoadOp.Load, StoreOp.Store, default);

	/// <summary>
	/// Clears to <paramref name="color"/> and stores the rendered contents.
	/// </summary>
	public static ColorAttachmentOps Clear(Color32 color) => new(LoadOp.Clear, StoreOp.Store, color);
}
