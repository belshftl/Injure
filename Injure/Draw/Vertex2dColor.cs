// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Injure.Primitives;

namespace Injure.Draw;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex2dColor(float x, float y, Color32 color) {
	public readonly float X = x;
	public readonly float Y = y;
	public readonly Color32 Color = color;

	public static readonly int Size = Unsafe.SizeOf<Vertex2dColor>();

	public Vertex2dColor(Vector2 xy, Color32 color) : this(xy.X, xy.Y, color) {}
	public static explicit operator Vector2(Vertex2dColor v) => new(v.X, v.Y);
}
