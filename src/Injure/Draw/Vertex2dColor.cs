// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// A 2D vertex with a color, as taken by <see cref="Canvas"/>'s per-vertex-color shape draws.
/// </summary>
/// <param name="x">X position.</param>
/// <param name="y">
/// Y position. +Y is down, not up (before any further transformations by e.g. a transform matrix).
/// </param>
/// <param name="color">Color at this vertex; interpolated across the shape.</param>
/// <remarks>
/// The <see langword="default"/> value is valid and is a transparent vertex at the origin.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2dColor(float x, float y, SrgbColor32 color) {
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
	/// Color at this vertex.
	/// </summary>
	public readonly SrgbColor32 Color = color;

	/// <summary>
	/// Size of a <see cref="Vertex2dColor"/> in bytes.
	/// </summary>
	public static readonly int Size = Unsafe.SizeOf<Vertex2dColor>();

	/// <summary>
	/// Creates a vertex at <paramref name="xy"/>.
	/// </summary>
	/// <param name="xy">Position.</param>
	/// <param name="color">Color at this vertex; interpolated across the shape.</param>
	public Vertex2dColor(Vector2 xy, SrgbColor32 color) : this(xy.X, xy.Y, color) {}

	/// <summary>
	/// Gets the position of <paramref name="v"/>.
	/// </summary>
	public static explicit operator Vector2(Vertex2dColor v) => new(v.X, v.Y);
}
