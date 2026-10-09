// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How vertices are assembled into primitives.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is valid and is <see cref="Undefined"/>.
/// </remarks>
[ClosedEnum]
[ClosedEnumMirror(typeof(WGPUPrimitiveTopology))]
public readonly partial struct PrimitiveTopology {
	/// <summary>Raw switch tag for <see cref="PrimitiveTopology"/>.</summary>
	public enum Case {
		/// <summary>
		/// No value; WebGPU uses <see cref="TriangleList"/>.
		/// </summary>
		Undefined = 0,

		/// <summary>
		/// Each vertex is a point.
		/// </summary>
		PointList = 1,

		/// <summary>
		/// Each pair of vertices is a line.
		/// </summary>
		LineList = 2,

		/// <summary>
		/// Each vertex after the first continues a connected line.
		/// </summary>
		LineStrip = 3,

		/// <summary>
		/// Each triple of vertices is a triangle.
		/// </summary>
		TriangleList = 4,

		/// <summary>
		/// Each vertex after the first two forms a triangle with the previous two.
		/// </summary>
		TriangleStrip = 5,
	}
}
