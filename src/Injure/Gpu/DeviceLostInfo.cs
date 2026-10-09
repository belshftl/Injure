// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;

namespace Injure.Gpu;

/// <summary>
/// The lifecycle state of a <see cref="GpuDevice"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DeviceState {
	/// <summary>Raw switch tag for <see cref="DeviceState"/>.</summary>
	public enum Case {
		/// <summary>
		/// Usable.
		/// </summary>
		Alive = 1,

		/// <summary>
		/// Lost; see <see cref="DeviceLostException"/>.
		/// </summary>
		Lost,

		/// <summary>Disposed.</summary>
		Disposed,
	}
}

/// <summary>
/// How certain a <see cref="DeviceLostInfo"/> is.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct DeviceLossInfoKind {
	/// <summary>Raw switch tag for <see cref="DeviceLossInfoKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// Injure noticed the loss itself (e.g. a surface reported it) before WebGPU's device lost
		/// callback ran; the callback may still replace this info with a <see cref="Final"/> one.
		/// </summary>
		Provisional = 1,

		/// <summary>
		/// Reported by WebGPU's device lost callback.
		/// </summary>
		Final,
	}
}

/// <summary>
/// Why a <see cref="GpuDevice"/> was lost.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Unknown"/>.
/// </remarks>
[ClosedEnum]
public readonly partial struct DeviceLossEventReason {
	/// <summary>Raw switch tag for <see cref="DeviceLossEventReason"/>.</summary>
	public enum Case {
		/// <summary>
		/// WebGPU didn't say, e.g. after a driver crash or GPU reset.
		/// </summary>
		Unknown,

		/// <summary>
		/// The device was destroyed explicitly.
		/// </summary>
		Destroyed,

		/// <summary>
		/// The WebGPU instance the device belongs to was released.
		/// </summary>
		InstanceDropped,

		/// <summary>
		/// Creating the device failed.
		/// </summary>
		FailedCreation,

		/// <summary>
		/// A <see cref="SurfaceRenderOutput"/> got a device lost status while acquiring a texture.
		/// </summary>
		SurfaceAcquireDeviceLost,
	}
}

/// <summary>
/// Information about the loss of a <see cref="GpuDevice"/>.
/// </summary>
/// <param name="Kind">How certain this information is.</param>
/// <param name="Reason">Why the device was lost.</param>
/// <param name="Message">WebGPU's or Injure's description of the loss, if any.</param>
public sealed record DeviceLostInfo(
	DeviceLossInfoKind Kind,
	DeviceLossEventReason Reason,
	string? Message
);

/// <summary>
/// Thrown if a <see cref="GpuDevice"/> is used after it was lost.
/// </summary>
/// <param name="info">Information about the loss.</param>
/// <remarks>
/// A lost device can't be recovered; everything created from it has to be recreated on a new
/// device.
/// </remarks>
public sealed class DeviceLostException(DeviceLostInfo info) : Exception($"reason: {info.Reason}, message: {info.Message ?? "<no message provided>"}") {
	/// <summary>
	/// Information about the loss.
	/// </summary>
	public DeviceLostInfo Info { get; } = info;
}
