// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Injure.Primitives;

namespace Injure.Tests.Primitives;

public static class ColorTypeTests {
	[Fact]
	public static void ConversionsBetweenByteTypesKeepTheBytes() {
		RawColor32 raw = new(0xcb, 0xa6, 0xf7, 0x80);
		SrgbColor32 srgb = raw.AssumeSrgb();
		Assert.Equal((0xcb, 0xa6, 0xf7, 0x80), (srgb.R, srgb.G, srgb.B, srgb.A));
		Assert.Equal(raw, srgb.ToRaw());
	}

	[Fact]
	public static void ToRawF128MapsBytesToUnitRangeWithoutDecoding() {
		RawColorF128 expected = new(0f, 0.2f, 1f, 128 / 255f);
		Assert.Equal(expected, new RawColor32(0, 51, 255, 128).ToRawF128());
		Assert.Equal(expected, new SrgbColor32(0, 51, 255, 128).ToRawF128());
	}

	[Fact]
	public static void ByteTypesShareHexFormatAndParsing() {
		Assert.Equal(new SrgbColor32(0xcb, 0xa6, 0xf7), SrgbColor32.Parse("#CBA6F7"));
		Assert.Equal(new RawColor32(0xcb, 0xa6, 0xf7, 0x12), RawColor32.Parse("cba6f712"));
		Assert.False(SrgbColor32.TryParse("#cba6f", out _));
		Assert.Equal("#CBA6F7", new SrgbColor32(0xcb, 0xa6, 0xf7).ToHexCode(includeAlpha: false, leadingHash: true));
		Assert.Equal(SrgbColor32.Green, SrgbColor32.Parse("00ff00"));
	}

	[Fact]
	public static void RawColor32HasDocumentedLayout() {
		Assert.Equal(4, RawColor32.Size);
		Assert.Equal(RawColor32.Size, Unsafe.SizeOf<RawColor32>());
		RawColor32 c = new(0x11, 0x22, 0x33, 0x44);
		Assert.Equal([0x11, 0x22, 0x33, 0x44], MemoryMarshal.AsBytes(new ReadOnlySpan<RawColor32>(in c)).ToArray());
		// little-endian platform observation, not a documented guarantee
		Assert.Equal(0x44332211u, c.ReinterpretToU32());
	}

	[Fact]
	public static void SrgbColor32HasDocumentedLayout() {
		Assert.Equal(4, SrgbColor32.Size);
		Assert.Equal(SrgbColor32.Size, Unsafe.SizeOf<SrgbColor32>());
		SrgbColor32 c = new(0x11, 0x22, 0x33, 0x44);
		Assert.Equal([0x11, 0x22, 0x33, 0x44], MemoryMarshal.AsBytes(new ReadOnlySpan<SrgbColor32>(in c)).ToArray());
		// little-endian platform observation, not a documented guarantee
		Assert.Equal(0x44332211u, c.ReinterpretToU32());
	}

	[Fact]
	public static void RawColorF128HasDocumentedLayout() {
		Assert.Equal(16, RawColorF128.Size);
		Assert.Equal(RawColorF128.Size, Unsafe.SizeOf<RawColorF128>());
		RawColorF128 c = new(1f, 2f, 3f, 4f);
		Assert.Equal([1f, 2f, 3f, 4f], MemoryMarshal.Cast<RawColorF128, float>(new ReadOnlySpan<RawColorF128>(in c)).ToArray());
		Assert.Equal(new Vector4(1f, 2f, 3f, 4f), Unsafe.BitCast<RawColorF128, Vector4>(c));
	}

	[Fact]
	public static void SpanReinterpretsAliasAndKeepTheBytes() {
		RawColor32[] raw = [new(0x11, 0x22, 0x33, 0x44), new(0x55, 0x66, 0x77, 0x88)];

		ReadOnlySpan<SrgbColor32> srgbRo = RawColor32.SpanAssumeSrgb((ReadOnlySpan<RawColor32>)raw);
		Assert.Equal(raw.Length, srgbRo.Length);
		ref byte srgbStart = ref Unsafe.As<SrgbColor32, byte>(ref MemoryMarshal.GetReference(srgbRo));
		Assert.True(Unsafe.AreSame(ref Unsafe.As<RawColor32, byte>(ref raw[0]), ref srgbStart));
		Assert.Equal((0x55, 0x66, 0x77, 0x88), (srgbRo[1].R, srgbRo[1].G, srgbRo[1].B, srgbRo[1].A));

		// writes through either view land in the same memory
		Span<SrgbColor32> srgb = RawColor32.SpanAssumeSrgb(raw.AsSpan());
		srgb[0] = new SrgbColor32(0xaa, 0xbb, 0xcc, 0xdd);
		Assert.Equal(new RawColor32(0xaa, 0xbb, 0xcc, 0xdd), raw[0]);
		Span<RawColor32> back = SrgbColor32.SpanToRaw(srgb);
		Assert.Equal(raw.Length, back.Length);
		back[1] = new RawColor32(0x01, 0x02, 0x03, 0x04);
		Assert.Equal(new SrgbColor32(0x01, 0x02, 0x03, 0x04), srgb[1]);

		ReadOnlySpan<RawColor32> backRo = SrgbColor32.SpanToRaw((ReadOnlySpan<SrgbColor32>)srgb);
		Assert.True(Unsafe.AreSame(ref raw[0], ref MemoryMarshal.GetReference(backRo)));
		Assert.Equal(raw, backRo.ToArray());

		Assert.True(RawColor32.SpanAssumeSrgb(Span<RawColor32>.Empty).IsEmpty);
		Assert.True(SrgbColor32.SpanToRaw(ReadOnlySpan<SrgbColor32>.Empty).IsEmpty);
	}

	[Fact]
	public static void DefaultsAreTransparent() {
		Assert.Equal(RawColor32.Transparent, default);
		Assert.Equal(SrgbColor32.Transparent, default);
		Assert.Equal(RawColorF128.Transparent, default);
	}

	[Fact]
	public static void FloatTypeRoundtripsThroughVector4() {
		Vector4 v = new(2f, -0.5f, 0.25f, 1f);
		var c = RawColorF128.FromVector4(v);
		Assert.Equal((2f, -0.5f, 0.25f, 1f), (c.R, c.G, c.B, c.A));
		Assert.Equal(v, c.ToVector4());
		RawColorF128 nan = new(float.NaN, 0f, 0f);
		Assert.Equal(nan, nan);
	}
}
