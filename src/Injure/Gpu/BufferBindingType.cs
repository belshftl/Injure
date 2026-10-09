// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// The kind of buffer a <see cref="GpuBufferBindingLayout"/> binds.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUBufferBindingType), Subset = true)]
public readonly partial struct BufferBindingType {
	/// <summary>Raw switch tag for <see cref="BufferBindingType"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="Uniform"/>.
		/// </summary>
		Undefined = 1,

		/// <summary>
		/// A uniform buffer (<c>var&lt;uniform&gt;</c>); requires <see cref="BufferUsage.Uniform"/>.
		/// </summary>
		Uniform = 2,

		/// <summary>
		/// A read-write storage buffer (<c>var&lt;storage, read_write&gt;</c>); requires
		/// <see cref="BufferUsage.Storage"/>.
		/// </summary>
		Storage = 3,

		/// <summary>
		/// A read-only storage buffer (<c>var&lt;storage, read&gt;</c>); requires
		/// <see cref="BufferUsage.Storage"/>.
		/// </summary>
		ReadOnlyStorage = 4,
	}
}
