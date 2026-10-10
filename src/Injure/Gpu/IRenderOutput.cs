// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Injure.Gpu;

/// <summary>
/// A destination that frames are rendered to and presented on, such as a window's swapchain
/// (<see cref="SurfaceRenderOutput"/>) or an offscreen texture that's handed to something else.
/// </summary>
/// <remarks>
/// Color-only. Depth/stencil usage should use offscreen render targets instead.
/// </remarks>
public interface IRenderOutput : IDisposable {
	/// <summary>
	/// Current width in physical pixels.
	/// </summary>
	uint Width { get; }

	/// <summary>
	/// Current height in physical pixels.
	/// </summary>
	uint Height { get; }

	/// <summary>
	/// Color format.
	/// </summary>
	TextureFormat Format { get; }

	/// <summary>
	/// Attempts to acquire the image to render the next frame into.
	/// </summary>
	/// <param name="output">On success, the acquired image, owned by the caller.</param>
	/// <returns>
	/// <see langword="true"/> if an image was acquired; <see langword="false"/> if this frame
	/// should be skipped.
	/// </returns>
	/// <remarks>
	/// Recoverable acquire failures (e.g. a swapchain that's being resized) return
	/// <see langword="false"/>. Fatal failures throw.
	/// </remarks>
	bool TryAcquire([NotNullWhen(true)] out IAcquiredOutput? output);
}

/// <summary>
/// An image acquired from an <see cref="IRenderOutput"/> for one frame.
/// </summary>
/// <remarks>
/// <para>
/// The intended sequence is: record rendering into <see cref="View"/>, call
/// <see cref="RecordFinalCommands(GpuCommandEncoderHandle)"/> on the same encoder, finish and
/// submit that encoder, call <see cref="Present()"/>, then dispose. <see cref="RenderFrame"/> does
/// this. Disposing without presenting drops the frame.
/// </para>
/// <para>
/// Implementations may assume the methods are called in that order, at most once each, from one
/// thread.
/// </para>
/// </remarks>
public interface IAcquiredOutput : IDisposable {
	/// <summary>
	/// The color view to render the frame into.
	/// </summary>
	/// <remarks>
	/// Has <see cref="TextureUsage.RenderAttachment"/> and is 2D, with the owning output's
	/// <see cref="IRenderOutput.Width"/>, <see cref="IRenderOutput.Height"/> and
	/// <see cref="IRenderOutput.Format"/> as of acquisition. Valid until disposal.
	/// </remarks>
	GpuTextureViewRef View { get; }

	/// <summary>
	/// Records commands this output needs to run after the frame's rendering, such as copying
	/// <see cref="View"/>'s texture into a readback buffer.
	/// </summary>
	/// <param name="encoder">
	/// The encoder the frame's rendering was recorded into, with no render pass active.
	/// </param>
	/// <remarks>
	/// The default implementation is a no-op.
	/// </remarks>
	void RecordFinalCommands(GpuCommandEncoderHandle encoder) {
	}

	/// <summary>
	/// Presents the frame, after the commands rendering it have been submitted.
	/// </summary>
	void Present();
}
