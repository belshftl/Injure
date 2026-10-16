// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.InteropServices;
using Injure.Primitives;

namespace Injure.Draw;

/// <summary>
/// A 2D vertex with a color.
/// </summary>
/// <param name="x">X position.</param>
/// <param name="y">
/// Y position. +Y is down, not up (before any further transformations by e.g. a transform matrix).
/// </param>
/// <param name="color">Color at this vertex; interpolated across the shape.</param>
/// <remarks>
/// <para>
/// Laid out in memory as <see cref="float"/>s <see cref="X"/>, <see cref="Y"/>, then the 4 bytes of
/// <see cref="Color"/>, in that order, with no padding. The memory layout is fixed, not incidental.
/// Safe for FFI, <c>stackalloc Vertex2dColor[]</c>, bit/span reinterprets, etc.
/// </para>
/// <para>
/// The <see langword="float"/> components aren't validated to be finite; since this is a blittable
/// primitive, meaningful validation can't happen. As such, a component may be NaN or infinite.
/// </para>
/// <para>
/// The <see langword="default"/> value is valid and is a transparent vertex at position (0, 0).
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public readonly struct Vertex2dColor(float x, float y, SrgbColor32 color) {
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
	/// Color at this vertex, at byte offset 8.
	/// </summary>
	[FieldOffset(8)] public readonly SrgbColor32 Color = color;

	/// <summary>
	/// Size of a <see cref="Vertex2dColor"/> in bytes. Equal to 12.
	/// </summary>
	public const int Size = 12;

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
