// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Gpu;

/// <summary>
/// Layout of one vertex buffer slot used by a render pipeline.
/// </summary>
/// <param name="ArrayStride">Stride of one vertex in bytes.</param>
/// <param name="StepMode">Whether the buffer advances per vertex or per instance.</param>
/// <param name="Attributes">Vertex attribute declarations.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is an unused vertex buffer slot
/// (<see cref="VertexStepMode.VertexBufferNotUsed"/>).
/// </remarks>
public readonly record struct VertexBufferLayout(
	ulong ArrayStride,
	VertexStepMode StepMode,
	ImmutableArray<VertexAttribute> Attributes
);
