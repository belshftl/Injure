// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How presented frames are synchronized with the display.
/// </summary>
/// <remarks>
/// <para>
/// Not every mode is supported everywhere; <see cref="SurfaceRenderOutput"/> picks one through a
/// <see cref="SurfacePresentModePolicy"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </para>
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUPresentMode))]
public readonly partial struct PresentMode {
	/// <summary>Raw switch tag for <see cref="PresentMode"/>.</summary>
	public enum Case {
		/// <summary>No value.</summary>
		Undefined = 0,

		/// <summary>
		/// Frames are queued and shown one per vblank. Never tears; supported everywhere.
		/// </summary>
		Fifo = 1,

		/// <summary>
		/// Like <see cref="Fifo"/>, but a late frame is shown immediately, which may tear.
		/// </summary>
		FifoRelaxed = 2,

		/// <summary>
		/// Frames are shown immediately. May tear; lowest latency.
		/// </summary>
		Immediate = 3,

		/// <summary>
		/// Frames replace the queued one and are shown at the next vblank. Never tears.
		/// </summary>
		Mailbox = 4,
	}
}
