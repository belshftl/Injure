// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// A set of shader stages, e.g. the ones a binding is visible to.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="None"/>.
/// </remarks>
[ClosedFlags]
[ClosedFlagsMirror(typeof(WGPUShaderStage))]
public readonly partial struct ShaderStage {
	/// <summary>Raw bits for <see cref="ShaderStage"/>.</summary>
	[Flags]
	public enum Bits : ulong {
		/// <summary>No stages.</summary>
		None = 0ul,

		/// <summary>The vertex stage.</summary>
		Vertex = 1ul,

		/// <summary>The fragment stage.</summary>
		Fragment = 2ul,

		/// <summary>
		/// The compute stage; compute isn't supported yet.
		/// </summary>
		Compute = 4ul,
	}
}
