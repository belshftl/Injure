// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How a shader may access a texture bound by a <see cref="GpuStorageTextureBindingLayout"/>.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUStorageTextureAccess), Subset = true)]
public readonly partial struct StorageTextureAccess {
	/// <summary>Raw switch tag for <see cref="StorageTextureAccess"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="WriteOnly"/>.
		/// </summary>
		Undefined = 1,

		/// <summary>Write-only access.</summary>
		WriteOnly = 2,

		/// <summary>Read-only access.</summary>
		ReadOnly = 3,

		/// <summary>
		/// Read-write access; only supported for some formats.
		/// </summary>
		ReadWrite = 4,
	}
}
