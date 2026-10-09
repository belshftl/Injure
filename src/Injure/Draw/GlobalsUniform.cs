// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Injure.Draw;

[StructLayout(LayoutKind.Sequential)]
internal struct GlobalsUniform {
	public Matrix4x4 Projection;

	public static readonly int Size = Unsafe.SizeOf<GlobalsUniform>();
}
