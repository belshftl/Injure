// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.InteropServices;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// A 2D vertex with texture UV coordinates and a color.
/// </summary>
/// <param name="x">X position.</param>
/// <param name="y">
/// Y position. +Y is down, not up (before any further transformations by e.g. a transform matrix).
/// </param>
/// <param name="u">Texture U coordinate, from 0 at the left edge to 1 at the right.</param>
/// <param name="v">Texture V coordinate, from 0 at the top edge to 1 at the bottom.</param>
/// <param name="color">Color at this vertex, multiplied with the sampled texel.</param>
/// <remarks>
/// <para>
/// Laid out in memory as <see cref="float"/>s <see cref="X"/>, <see cref="Y"/>, <see cref="U"/>,
/// <see cref="V"/>, then the 4 bytes of <see cref="Color"/>, in that order, with no padding. The
/// memory layout is fixed, not incidental. Safe for FFI, <c>stackalloc Vertex2dTextureColor[]</c>,
/// bit/span reinterprets, etc.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is a transparent vertex at position (0, 0) and
/// texture UV coordinates (0, 0).
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly struct Vertex2dTextureColor(float x, float y, float u, float v, SrgbColor32 color) {
	/// <summary>
	/// X position, at byte offset 0.
	/// </summary>
	[FieldOffset(0)] public readonly float X = x;

	/// <summary>
	/// Y position, at byte offset 4. +Y is down, not up (before any further transformations by e.g. a
	/// transform matrix).
	/// </summary>
	[FieldOffset(4)] public readonly float Y = y;

	/// <summary>
	/// Texture U coordinate, at byte offset 8.
	/// </summary>
	[FieldOffset(8)] public readonly float U = u;

	/// <summary>
	/// Texture V coordinate, at byte offset 12.
	/// </summary>
	[FieldOffset(12)] public readonly float V = v;

	/// <summary>
	/// Color at this vertex, at byte offset 16.
	/// </summary>
	[FieldOffset(16)] public readonly SrgbColor32 Color = color;

	/// <summary>
	/// Size of a <see cref="Vertex2dTextureColor"/> in bytes. Equal to 20.
	/// </summary>
	public const int Size = 20;

	/// <summary>
	/// Creates a vertex at <paramref name="xy"/> with UV <paramref name="uv"/>.
	/// </summary>
	/// <param name="xy">Position.</param>
	/// <param name="uv">Texture UV coordinates.</param>
	/// <param name="color">Color at this vertex, multiplied with the sampled texel.</param>
	public Vertex2dTextureColor(Vector2 xy, Vector2 uv, SrgbColor32 color) : this(xy.X, xy.Y, uv.X, uv.Y, color) {}

	/// <summary>
	/// Creates a vertex at <paramref name="xy"/> with UV <paramref name="u"/> + <paramref name="v"/>.
	/// </summary>
	/// <param name="xy">Position.</param>
	/// <param name="u">Texture U coordinate.</param>
	/// <param name="v">Texture V coordinate.</param>
	/// <param name="color">Color at this vertex, multiplied with the sampled texel.</param>
	public Vertex2dTextureColor(Vector2 xy, float u, float v, SrgbColor32 color) : this(xy.X, xy.Y, u, v, color) {}
}
