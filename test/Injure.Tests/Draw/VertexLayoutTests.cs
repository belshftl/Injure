// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Injure.Draw;
using Injure.Primitives;

namespace Injure.Tests.Draw;

public static class VertexLayoutTests {
	[Fact]
	public static void Vertex2dColorHasDocumentedLayout() {
		Assert.Equal(Vertex2dColor.Size, Unsafe.SizeOf<Vertex2dColor>());
		Vertex2dColor v = new(1f, 2f, new SrgbColor32(0x11, 0x22, 0x33, 0x44));
		ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<Vertex2dColor>(in v));
		Assert.Equal(1f, BitConverter.ToSingle(bytes[0..]));
		Assert.Equal(2f, BitConverter.ToSingle(bytes[4..]));
		Assert.Equal([0x11, 0x22, 0x33, 0x44], bytes[8..12].ToArray());
	}

	[Fact]
	public static void Vertex2dTextureColorHasDocumentedLayout() {
		Assert.Equal(Vertex2dTextureColor.Size, Unsafe.SizeOf<Vertex2dTextureColor>());
		Vertex2dTextureColor v = new(1f, 2f, 3f, 4f, new SrgbColor32(0x11, 0x22, 0x33, 0x44));
		ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<Vertex2dTextureColor>(in v));
		Assert.Equal(1f, BitConverter.ToSingle(bytes[0..]));
		Assert.Equal(2f, BitConverter.ToSingle(bytes[4..]));
		Assert.Equal(3f, BitConverter.ToSingle(bytes[8..]));
		Assert.Equal(4f, BitConverter.ToSingle(bytes[12..]));
		Assert.Equal([0x11, 0x22, 0x33, 0x44], bytes[16..20].ToArray());
	}
}
