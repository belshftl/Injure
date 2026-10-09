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
	/// Called again whenever the surface needs to be recreated (e.g. after it was lost), so
	/// implementations whose native object can change should return the current one.
	/// </remarks>
	SurfaceSource GetSurfaceSource();

	/// <summary>
	/// Gets the current drawable size of the host surface in physical pixels.
	/// </summary>
	/// <remarks>
	/// May differ from the logical size on HiDPI setups.
	/// </remarks>
	(uint Width, uint Height) GetDrawableSize();
}
