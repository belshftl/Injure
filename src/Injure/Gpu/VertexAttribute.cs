// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes one vertex attribute within a vertex buffer layout.
/// </summary>
/// <param name="Format">Vertex element format.</param>
/// <param name="Offset">Byte offset of the attribute within one vertex.</param>
/// <param name="ShaderLocation">Shader location consumed by this attribute.</param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Format"/> is
/// <see cref="VertexFormat.None"/>.
/// </remarks>
public readonly record struct VertexAttribute(
	VertexFormat Format,
	ulong Offset,
	uint ShaderLocation
) {
	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUVertexAttribute"/>.
	/// </summary>
	internal WGPUVertexAttribute ToWebgpuType() => new(
		format: Format.ToWebgpuType(),
		offset: Offset,
		shaderLocation: ShaderLocation
	);
}
