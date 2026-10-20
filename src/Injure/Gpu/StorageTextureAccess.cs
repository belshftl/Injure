// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a shader may access a texture bound by a <see cref="GpuStorageTextureBindingLayout"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <c>read</c> and <c>read_write</c> access modes in WGSL, which <see cref="ReadOnly"/> and
/// <see cref="ReadWrite"/> need, come from the WGSL language extension
/// <c>readonly_and_readwrite_storage_textures</c>. Language extensions aren't device features (the
/// ones requested through <see cref="GpuDeviceOptions"/>), so there's nothing to manually enable,
/// and shaders can use them without an <c>enable</c> directive if the implementation supports
/// them; see <see href="https://www.w3.org/TR/WGSL/#language-extensions-sec"/>.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUStorageTextureAccess), Subset = true)]
public readonly partial struct StorageTextureAccess {
	/// <summary>Raw switch tag for <see cref="StorageTextureAccess"/>.</summary>
	public enum Case {
		/// <summary>
		/// Write-only access; the shader declares the texture with the <c>write</c> access mode.
		/// </summary>
		WriteOnly = 2,

		/// <summary>
		/// Read-only access; the shader declares the texture with the <c>read</c> access mode.
		/// </summary>
		ReadOnly = 3,

		/// <summary>
		/// Read-write access; the shader declares the texture with the <c>read_write</c> or
		/// <c>write</c> access mode. Only supported for <see cref="TextureFormat.R32Float"/>,
		/// <see cref="TextureFormat.R32Sint"/>, and <see cref="TextureFormat.R32Uint"/>.
		/// </summary>
		ReadWrite = 4,
	}
}
