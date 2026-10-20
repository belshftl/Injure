// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Gpu;

/// <summary>
/// Layout of one vertex buffer slot used by a render pipeline.
/// </summary>
/// <param name="ArrayStride">
/// Bytes between the starts of successive elements; must be a multiple of 4 and at most
/// <see cref="GpuLimits.MaxVertexBufferArrayStride"/>. With 0, every vertex or instance reads the
/// same element.
/// </param>
/// <param name="StepMode">Whether the buffer advances per vertex or per instance.</param>
/// <param name="Attributes">Attributes read from each element.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is an unused vertex buffer slot
/// (<see cref="VertexStepMode.VertexBufferNotUsed"/>).
/// </remarks>
public readonly record struct VertexBufferLayout(
	ulong ArrayStride,
	VertexStepMode StepMode,
	ImmutableArray<VertexAttribute> Attributes
);
