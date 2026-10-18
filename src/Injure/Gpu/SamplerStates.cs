// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// Common <see cref="GpuSamplerCreateParams"/>.
/// </summary>
public static class SamplerStates {
	/// <summary>
	/// <see cref="AddressMode.ClampToEdge"/> for the address mode on all three axes (U/V/W),
	/// <see cref="FilterMode.Nearest"/> for the mag/min filters, and
	/// <see cref="MipmapFilterMode.Nearest"/> for the mipmap filter.
	/// </summary>
	public static readonly GpuSamplerCreateParams NearestClamp = new(
		AddressModeU: AddressMode.ClampToEdge,
		AddressModeV: AddressMode.ClampToEdge,
		AddressModeW: AddressMode.ClampToEdge,
		MagFilter: FilterMode.Nearest,
		MinFilter: FilterMode.Nearest,
		MipmapFilter: MipmapFilterMode.Nearest
	);

	/// <summary>
	/// <see cref="AddressMode.ClampToEdge"/> for the address mode on all three axes (U/V/W),
	/// <see cref="FilterMode.Linear"/> for the mag/min filters, and
	/// <see cref="MipmapFilterMode.Linear"/> for the mipmap filter.
	/// </summary>
	public static readonly GpuSamplerCreateParams LinearClamp = new(
		AddressModeU: AddressMode.ClampToEdge,
		AddressModeV: AddressMode.ClampToEdge,
		AddressModeW: AddressMode.ClampToEdge,
		MagFilter: FilterMode.Linear,
		MinFilter: FilterMode.Linear,
		MipmapFilter: MipmapFilterMode.Linear
	);

	/// <summary>
	/// <see cref="AddressMode.Repeat"/> for the address mode on all three axes (U/V/W),
	/// <see cref="FilterMode.Nearest"/> for the mag/min filters, and
	/// <see cref="MipmapFilterMode.Nearest"/> for the mipmap filter.
	/// </summary>
	public static readonly GpuSamplerCreateParams NearestRepeat = new(
		AddressModeU: AddressMode.Repeat,
		AddressModeV: AddressMode.Repeat,
		AddressModeW: AddressMode.Repeat,
		MagFilter: FilterMode.Nearest,
		MinFilter: FilterMode.Nearest,
		MipmapFilter: MipmapFilterMode.Nearest
	);

	/// <summary>
	/// <see cref="AddressMode.Repeat"/> for the address mode on all three axes (U/V/W),
	/// <see cref="FilterMode.Linear"/> for the mag/min filters, and
	/// <see cref="MipmapFilterMode.Linear"/> for the mipmap filter.
	/// </summary>
	public static readonly GpuSamplerCreateParams LinearRepeat = new(
		AddressModeU: AddressMode.Repeat,
		AddressModeV: AddressMode.Repeat,
		AddressModeW: AddressMode.Repeat,
		MagFilter: FilterMode.Linear,
		MinFilter: FilterMode.Linear,
		MipmapFilter: MipmapFilterMode.Linear
	);
}
