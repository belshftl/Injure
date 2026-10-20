// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Injure.DevAnalyzers.Attributes;
using WebGPU;

namespace Injure.Gpu;

/// <summary>
/// Which winding order makes a triangle front-facing.
/// </summary>
/// <remarks>
/// <para>
/// WebGPU determines the winding after clipping and the viewport transform, from the framebuffer
/// coordinates of the triangle's vertices, in the order the primitive lists them; see
/// <see href="https://www.w3.org/TR/webgpu/#polygon-rasterization"/>. It computes
/// <c>area = 0.5 * sum(v[i + 1].x * v[i].y - v[i].x * v[i + 1].y)</c> over the triangle's edges,
/// with indices modulo 3, and classifies a positive area as counter-clockwise (hereafter "ccw")
/// and a negative area as clockwise (hereafter "cw").
/// </para>
/// <para>
/// This formula negates the standard shoelace formula,
/// <c>0.5 * sum(v[i].x * v[i + 1].y - v[i + 1].x * v[i].y)</c>. The meaning of the standard
/// formula's sign depends on the axis convention: a positive value means ccw as the triangle
/// appears if +Y points up, and cw as the triangle appears if +Y points down. +Y points down in
/// framebuffer coordinates, so after the negation, ccw means ccw as the triangle appears in the
/// render target. The viewport transform flips Y and applies a positive scale and an offset, so a
/// triangle is ccw exactly when the standard formula is positive for its NDC coordinates, where
/// +Y points up.
/// </para>
/// <para>
/// Flipping Y in the vertex shader negates the standard formula, but doesn't change the winding as
/// the triangle appears if the flip only converts a +Y-down input space to +Y-up NDC, since the
/// coordinates and the direction of +Y flip together. For example, a 2D orthographic projection
/// whose input is pixel coordinates for a <c>w</c> x <c>h</c> target, with the origin at the
/// top-left and +Y pointing down, computes <c>ndc = (2 * x / w - 1, 1 - 2 * y / h)</c>. The
/// viewport transform flips Y again, so the framebuffer coordinates equal the pixel coordinates up
/// to the viewport's positive scale and offset, and the triangle appears in the render target as
/// it does in pixel coordinates. A triangle that is ccw in pixel coordinates is therefore also
/// considered ccw by WebGPU. Given <c>v0 = (1.5, 1)</c>, <c>v1 = (1, 2)</c>, <c>v2 = (2, 2)</c>
/// in pixel coordinates, the standard formula gives <c>-0.5</c> over the pixel coordinates
/// (ccw, since +Y points down) and <c>2 / (w * h)</c> over the NDC-converted coordinates (ccw,
/// since +Y points up); with a viewport covering the whole target, WebGPU's formula gives
/// <c>0.5</c> over framebuffer coordinates (ccw). The winding reverses only if the transforms
/// mirror the image relative to the input space, such as when the same projection receives +Y-up
/// coordinates.
/// </para>
/// <para>
/// A degenerate triangle is considered back-facing under both <see cref="Ccw"/> and
/// <see cref="Cw"/>, since each counts only an area of its own sign as front-facing.
/// </para>
/// <para>
/// The <see langword="default"/> value is invalid.
/// </para>
/// </remarks>
[ClosedEnum(DefaultIsInvalid = true)]
[ClosedEnumMirror(typeof(WGPUFrontFace), Subset = true)]
public readonly partial struct FrontFace {
	/// <summary>Raw switch tag for <see cref="FrontFace"/>.</summary>
	public enum Case {
		/// <summary>
		/// Triangles whose vertices appear counter-clockwise in the render target, i.e. ones with a
		/// positive area (see type-level docs), are front-facing.
		/// </summary>
		Ccw = 1,

		/// <summary>
		/// Triangles whose vertices appear clockwise in the render target, i.e. ones with a negative
		/// area (see type-level docs), are front-facing.
		/// </summary>
		Cw = 2,
	}
}
