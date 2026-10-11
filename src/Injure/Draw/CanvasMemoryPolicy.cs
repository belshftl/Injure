// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Draw;

/// <summary>
/// How long canvases keep around GPU memory they don't need anymore, in case it turns out after a
/// bit more time they actually need it again; set per device with
/// <see cref="OwnedCanvas.SetMemoryPolicy(Gpu.GpuDevice, CanvasMemoryPolicy)"/>.
/// </summary>
/// <param name="RetainForCanvases">
/// How many canvases in a row memory has to go unused in before it's released. Must not be
/// negative.
/// </param>
/// <param name="RetainForTime">
/// How long memory has to go unused before it's released. Must not be negative.
/// </param>
/// <remarks>
/// <para>
/// Canvases keep the buffers they draw into and reuse them for later canvases, growing them when a
/// canvas draws more than ever before. Memory is released once it has gone unused for at least
/// <paramref name="RetainForCanvases"/> canvases and at least <paramref name="RetainForTime"/>.
/// Higher values avoid reallocating when the amount drawn fluctuates; lower values release memory
/// sooner after a temporary peak. Requiring both means a high frame rate doesn't release memory
/// within a fraction of a second, and a low one doesn't release it based on only a few frames.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and releases memory as soon as a canvas didn't use
/// it.
/// </para>
/// </remarks>
public readonly record struct CanvasMemoryPolicy(int RetainForCanvases, TimeSpan RetainForTime) {
	/// <summary>
	/// The policy devices start with: 120 canvases (about two seconds at 60 FPS and 1 canvas/frame)
	/// and 5 seconds.
	/// </summary>
	/// <remarks>
	/// 1 canvas/frame is almost always the case. Having more than 1 canvas per frame would require
	/// a custom setup.
	/// </remarks>
	public static readonly CanvasMemoryPolicy Default = new(120, TimeSpan.FromSeconds(5));
}
