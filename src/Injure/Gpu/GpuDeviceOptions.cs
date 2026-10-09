// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// Configuration for a <see cref="GpuDevice"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is a device with no features, no adapter
/// preferences, and no compatibility requirement.
/// </remarks>
public readonly struct GpuDeviceOptions {
	/// <summary>
	/// Features the device must have; creating it throws if the adapter lacks any of them.
	/// </summary>
	public required GpuFeatures RequiredFeatures { get; init; }

	/// <summary>
	/// Features to enable if the adapter supports them. Check <see cref="GpuDevice.Features"/>
	/// for which ones were enabled.
	/// </summary>
	public GpuFeatures OptionalFeatures { get; init; }

	/// <summary>
	/// Which kind of GPU to prefer when several are available.
	/// </summary>
	public PowerPreference PowerPreference { get; init; } = PowerPreference.HighPerformance;

	/// <summary>
	/// Graphics API to use; <see cref="BackendType.Undefined"/> (the default) lets WebGPU pick.
	/// </summary>
	public BackendType BackendType { get; init; }

	/// <summary>
	/// If not <see langword="null"/>, the adapter is chosen to be able to present to a surface on
	/// this host. A temporary surface is created on it for that purpose and released before the
	/// <see cref="GpuDevice"/> constructor returns.
	/// </summary>
	public ISurfaceHost? CompatibleHost { get; init; }

	/// <summary>
	/// Creates options with the defaults described on each property; <see cref="RequiredFeatures"/>
	/// still has to be set.
	/// </summary>
	public GpuDeviceOptions() {
	}
}
