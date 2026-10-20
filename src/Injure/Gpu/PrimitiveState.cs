// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Describes how a render pipeline should construct and rasterize primitives.
/// </summary>
/// <param name="Topology">Primitive topology.</param>
/// <param name="StripIndexFormat">
/// Index format of indexed draws with a strip topology; must match the format passed to
/// <see cref="RenderPass.SetIndexBuffer(GpuBufferHandle, IndexFormat, ulong, ulong)"/>. Can be
/// <see cref="IndexFormat.Undefined"/> for strip pipelines that are only used for non-indexed
/// draws, and must be for non-strip topologies.
/// </param>
/// <param name="FrontFace">Front-face winding rule.</param>
/// <param name="CullMode">Face culling mode.</param>
/// <param name="UnclippedDepth">
/// Whether primitives outside the [0, 1] depth range are kept, with their depth clamped, rather than
/// clipped; requires <see cref="GpuFeatures.DepthClipControl"/>.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is invalid. <c>new PrimitiveState()</c> is a triangle list
/// with counter-clockwise front faces, no culling, and depth clipping.
/// </remarks>
public readonly record struct PrimitiveState(
	PrimitiveTopology Topology,
	IndexFormat StripIndexFormat,
	FrontFace FrontFace,
	CullMode CullMode,
	bool UnclippedDepth
) : IHasInvalidDefault {
	/// <summary>
	/// Creates a triangle list state with counter-clockwise front faces, no culling, and depth
	/// clipping.
	/// </summary>
	public PrimitiveState() : this(PrimitiveTopology.TriangleList) {}

	/// <summary>
	/// Creates a state with the given topology, counter-clockwise front faces, no culling, and
	/// depth clipping.
	/// </summary>
	public PrimitiveState(PrimitiveTopology Topology) : this(Topology, IndexFormat.Undefined, FrontFace.Ccw, CullMode.None, false) {
	}

	/// <summary>
	/// Creates a state with the given topology, winding, culling, and depth clipping.
	/// </summary>
	public PrimitiveState(PrimitiveTopology Topology, FrontFace FrontFace, CullMode CullMode) :
		this(Topology, IndexFormat.Undefined, FrontFace, CullMode, false) {
	}

	internal WGPUPrimitiveState ToWebgpuType() => new() {
		topology = Topology.ToWebgpuType(),
		stripIndexFormat = StripIndexFormat.ToWebgpuType(),
		frontFace = FrontFace.ToWebgpuType(),
		cullMode = CullMode.ToWebgpuType(),
		unclippedDepth = UnclippedDepth ? WGPUBool.True : WGPUBool.False,
	};

	bool IHasInvalidDefault.IsDefault => this == default;
}
