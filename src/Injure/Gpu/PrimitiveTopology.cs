// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// How vertices are assembled into primitives.
/// </summary>
/// <remarks>
/// The <see langword="default"/> value is invalid.
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUPrimitiveTopology), Subset = true)]
public readonly partial struct PrimitiveTopology {
	/// <summary>Raw switch tag for <see cref="PrimitiveTopology"/>.</summary>
	public enum Case {
		/// <summary>
		/// Each vertex is a point, rasterized as a single pixel.
		/// </summary>
		PointList = 1,

		/// <summary>
		/// Each pair of vertices is a line, rasterized one pixel wide.
		/// </summary>
		LineList = 2,

		/// <summary>
		/// Each vertex after the first continues a connected line, rasterized one pixel wide.
		/// </summary>
		/// <remarks>
		/// In indexed draws, an index with all bits set (<c>0xffff</c> or <c>0xffffffff</c>) ends the
		/// strip and starts a new one; see <see cref="PrimitiveState.StripIndexFormat"/>.
		/// </remarks>
		LineStrip = 3,

		/// <summary>
		/// Each triple of vertices is a triangle.
		/// </summary>
		TriangleList = 4,

		/// <summary>
		/// Each vertex after the first two forms a triangle with the previous two.
		/// </summary>
		/// <remarks>
		/// In indexed draws, an index with all bits set (<c>0xffff</c> or <c>0xffffffff</c>) ends the
		/// strip and starts a new one; see <see cref="PrimitiveState.StripIndexFormat"/>.
		/// </remarks>
		TriangleStrip = 5,
	}
}
