// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Gpu;

/// <summary>
/// Vertex stage state for a render pipeline.
/// </summary>
/// <param name="ShaderModule">Shader module containing the vertex entry point.</param>
/// <param name="EntryPoint">Name of the <c>@vertex</c> function in the module.</param>
/// <param name="Buffers">
/// Layouts of the vertex buffer slots; layout <c>i</c> describes the buffer bound to slot <c>i</c>
/// with <see cref="RenderPass.SetVertexBuffer(uint, GpuBufferHandle, ulong, ulong)"/>.
/// <see langword="default"/> or empty for none.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
public readonly record struct VertexState(
	GpuShaderModuleHandle ShaderModule,
	string EntryPoint,
	ImmutableArray<VertexBufferLayout> Buffers = default
) : IHasInvalidDefault {
	bool IHasInvalidDefault.IsDefault => this == default;
}
