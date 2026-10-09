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
/// <see cref="IndexFormat.Undefined"/> for strip
/// pipelines that are only used for non-indexed draws, and must be for non-strip topologies.
/// </param>
/// <param name="FrontFace">Front-face winding rule.</param>
/// <param name="CullMode">Face culling mode.</param>
/// <param name="UnclippedDepth">
/// Whether depth clipping is disabled; requires <see cref="GpuFeatures.DepthClipControl"/>.
/// </param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a triangle list with counter-clockwise front
/// faces and no culling, since WebGPU substitutes those for the <c>Undefined</c> members.
/// </remarks>
public readonly record struct PrimitiveState(
	PrimitiveTopology Topology,
	IndexFormat StripIndexFormat,
	FrontFace FrontFace,
	CullMode CullMode,
	bool UnclippedDepth
) {
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

	/// <summary>
	/// Converts this value to a native WebGPU <see cref="WGPUPrimitiveState"/>.
	/// </summary>
	internal WGPUPrimitiveState ToWebgpuType() => new() {
		topology = Topology.ToWebgpuType(),
		stripIndexFormat = StripIndexFormat.ToWebgpuType(),
		frontFace = FrontFace.ToWebgpuType(),
		cullMode = CullMode.ToWebgpuType(),
		unclippedDepth = UnclippedDepth ? WGPUBool.True : WGPUBool.False,
	};
}
