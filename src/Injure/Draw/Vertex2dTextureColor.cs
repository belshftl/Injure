// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// A 2D vertex with texture coordinates and a color; the vertex format of everything
/// <see cref="Canvas"/> draws.
/// </summary>
/// <param name="x">X position.</param>
/// <param name="y">
/// Y position. +Y is down, not up (before any further transformations by e.g. a transform matrix).
/// </param>
/// <param name="u">Texture U coordinate, from 0 at the left edge to 1 at the right.</param>
/// <param name="v">Texture V coordinate, from 0 at the top edge to 1 at the bottom.</param>
/// <param name="color">Color at this vertex, multiplied with the sampled texel.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a transparent vertex at the origin, sampling
/// the texture's top-left corner.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2dTextureColor(float x, float y, float u, float v, SrgbColor32 color) {
	/// <summary>
	/// X position.
	/// </summary>
	public readonly float X = x;

	/// <summary>
	/// Y position. +Y is down, not up (before any further transformations by e.g. a
	/// transform matrix).
	/// </summary>
	public readonly float Y = y;

	/// <summary>
	/// Texture U coordinate.
	/// </summary>
	public readonly float U = u;

	/// <summary>
	/// Texture V coordinate.
	/// </summary>
	public readonly float V = v;

	/// <summary>
	/// Color at this vertex.
	/// </summary>
	public readonly SrgbColor32 Color = color;

	/// <summary>
	/// Size of a <see cref="Vertex2dTextureColor"/> in bytes.
	/// </summary>
	public static readonly int Size = Unsafe.SizeOf<Vertex2dTextureColor>();
}
