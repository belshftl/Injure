// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

namespace Injure.Gpu;

/// <summary>
/// Common <see cref="GpuSamplerCreateParams"/>.
/// </summary>
public static class SamplerStates {
	/// <summary>
	/// Nearest filtering, clamping coordinates to the edge.
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
	/// Linear filtering (including between mip levels), clamping coordinates to the edge.
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
	/// Nearest filtering, repeating the texture.
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
	/// Linear filtering (including between mip levels), repeating the texture.
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
