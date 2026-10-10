// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// Describes a host-owned presentable surface.
/// </summary>
public interface ISurfaceHost {
	/// <summary>
	/// Gets the native object to create a WebGPU surface on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Called again whenever the surface needs to be recreated (e.g. after it was lost), so
	/// implementations whose native object can change should return the current one.
	/// </para>
	/// <para>
	/// The returned native object must be alive. If there's none (e.g. a UI framework destroyed
	/// the native view), throw instead; the exception reaches whoever triggered the call, such as
	/// the caller of <see cref="SurfaceRenderOutput.TryAcquire(out IAcquiredOutput?)"/>. Returning
	/// a stale handle isn't detected, and creating a surface on one can abort the process inside
	/// the native WebGPU implementation; wgpu-native does on, for example, X11.
	/// </para>
	/// <para>
	/// To avoid getting there, dispose surfaces created on a native object before destroying it.
	/// </para>
	/// </remarks>
	SurfaceSource GetSurfaceSource();

	/// <summary>
	/// Gets the current drawable size of the host surface in physical pixels.
	/// </summary>
	/// <remarks>
	/// <para>
	/// May differ from the logical size on HiDPI setups. The surface's textures get exactly this
	/// size, so it has to be the native object's actual size in pixels.
	/// </para>
	/// <para>
	/// For a <c>CAMetalLayer</c>, the layer's <c>contentsScale</c> also has to match the pixels per
	/// point of the view showing it, or macOS scales the presented image; see
	/// <see cref="MacosMetalLayer.ContentsScale"/>.
	/// </para>
	/// </remarks>
	(uint Width, uint Height) GetDrawableSize();
}
