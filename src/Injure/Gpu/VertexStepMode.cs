// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Whether a vertex buffer advances per vertex or per instance.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="VertexBufferNotUsed"/>.
/// </remarks>
[ClosedEnum(CheckZeroName = false)]
[ClosedEnumMirror(typeof(WGPUVertexStepMode))]
public readonly partial struct VertexStepMode {
	/// <summary>Raw switch tag for <see cref="VertexStepMode"/>.</summary>
	public enum Case {
		/// <summary>
		/// Marks the vertex buffer slot as unused.
		/// </summary>
		VertexBufferNotUsed = 0,

		/// <summary>
		/// No value; WebGPU uses <see cref="Vertex"/>.
		/// </summary>
		Undefined = 1,

		/// <summary>
		/// Advances once per vertex.
		/// </summary>
		Vertex = 2,

		/// <summary>
		/// Advances once per instance.
		/// </summary>
		Instance = 3,
	}
}
