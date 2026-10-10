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
	/// <remarks>
	/// The host's native object therefore has to exist when the device is created. UI frameworks
	/// that create native views lazily (e.g. Avalonia's <c>NativeControlHost</c>, which creates its
	/// view when the control is attached) need the device creation to wait for the view.
	/// </remarks>
	public ISurfaceHost? CompatibleHost { get; init; }

	/// <summary>
	/// Called for every error WebGPU reports on the device; <see langword="null"/> means
	/// <see cref="GpuErrorHandlers.FailFast"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// See <see cref="GpuDevice"/> for when and on which thread the handler is called.
	/// </para>
	/// <para>
	/// This must not throw; if it does, the process will be killed with
	/// <see cref="Environment.FailFast(string?, Exception?)"/> as to prevent it from unwinding across
	/// an FFI boundary.
	/// </para>
	/// </remarks>
	public GpuErrorHandler? ErrorHandler { get; init; } = GpuErrorHandlers.FailFast;

	/// <summary>
	/// Creates options with the defaults described on each property; <see cref="RequiredFeatures"/>
	/// still has to be set.
	/// </summary>
	public GpuDeviceOptions() {
	}
}
