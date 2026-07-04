// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

public static class SamplerStates {
	public static readonly GpuSamplerCreateParams NearestClamp = new(
		AddressModeU: AddressMode.ClampToEdge,
		AddressModeV: AddressMode.ClampToEdge,
		AddressModeW: AddressMode.ClampToEdge,
		MagFilter: FilterMode.Nearest,
		MinFilter: FilterMode.Nearest,
		MipmapFilter: MipmapFilterMode.Nearest
	);

	public static readonly GpuSamplerCreateParams LinearClamp = new(
		AddressModeU: AddressMode.ClampToEdge,
		AddressModeV: AddressMode.ClampToEdge,
		AddressModeW: AddressMode.ClampToEdge,
		MagFilter: FilterMode.Linear,
		MinFilter: FilterMode.Linear,
		MipmapFilter: MipmapFilterMode.Linear
	);

	public static readonly GpuSamplerCreateParams NearestRepeat = new(
		AddressModeU: AddressMode.Repeat,
		AddressModeV: AddressMode.Repeat,
		AddressModeW: AddressMode.Repeat,
		MagFilter: FilterMode.Nearest,
		MinFilter: FilterMode.Nearest,
		MipmapFilter: MipmapFilterMode.Nearest
	);

	public static readonly GpuSamplerCreateParams LinearRepeat = new(
		AddressModeU: AddressMode.Repeat,
		AddressModeV: AddressMode.Repeat,
		AddressModeW: AddressMode.Repeat,
		MagFilter: FilterMode.Linear,
		MinFilter: FilterMode.Linear,
		MipmapFilter: MipmapFilterMode.Linear
	);
}
