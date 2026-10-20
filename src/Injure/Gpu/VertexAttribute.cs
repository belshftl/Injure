// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes one vertex attribute within a vertex buffer layout.
/// </summary>
/// <param name="Format">Format of the attribute in the buffer, and the type the shader reads.</param>
/// <param name="Offset">
/// Byte offset of the attribute within one element of the buffer; must be a multiple of the
/// format's size or 4, whichever is smaller, and the attribute must fit in
/// <see cref="VertexBufferLayout.ArrayStride"/> unless that is 0.
/// </param>
/// <param name="ShaderLocation">
/// The <c>@location</c> of the vertex shader input that reads the attribute; must be unique across
/// all buffers of a pipeline and less than <see cref="GpuLimits.MaxVertexAttributes"/>.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid, since its <see cref="Format"/> is invalid.
/// </remarks>
public readonly record struct VertexAttribute(
	VertexFormat Format,
	ulong Offset,
	uint ShaderLocation
) : IHasInvalidDefault {
	internal WGPUVertexAttribute ToWebgpuType() => new(
		format: Format.ToWebgpuType(),
		offset: Offset,
		shaderLocation: ShaderLocation
	);

	bool IHasInvalidDefault.IsDefault => this == default;
}
