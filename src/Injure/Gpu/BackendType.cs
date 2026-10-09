// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// A graphics API that WebGPU can run on.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>, i.e. no preference.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUBackendType))]
public readonly partial struct BackendType {
	/// <summary>Raw switch tag for <see cref="BackendType"/>.</summary>
	public enum Case {
		/// <summary>
		/// No preference; WebGPU picks a backend.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// A backend that does nothing; for testing.
		/// </summary>
		Null = 1,

		/// <summary>
		/// A browser's WebGPU implementation.
		/// </summary>
		Webgpu = 2,

		/// <summary>Direct3D 11.</summary>
		D3d11 = 3,

		/// <summary>Direct3D 12.</summary>
		D3d12 = 4,

		/// <summary>Metal.</summary>
		Metal = 5,

		/// <summary>Vulkan.</summary>
		Vulkan = 6,

		/// <summary>OpenGL.</summary>
		Opengl = 7,

		/// <summary>OpenGL ES.</summary>
		Opengles = 8,
	}
}
