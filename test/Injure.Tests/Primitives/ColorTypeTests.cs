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
		SrgbStraight32 srgb = raw.AssumeSrgbStraight();
		Assert.Equal((0xcb, 0xa6, 0xf7, 0x80), (srgb.R, srgb.G, srgb.B, srgb.A));
		Assert.Equal(raw, srgb.ToRaw());
	}

	[Fact]
	public static void ToRawF128MapsBytesToUnitRangeWithoutDecoding() {
		RawColorF128 expected = new(0f, 0.2f, 1f, 128 / 255f);
		Assert.Equal(expected, new RawColor32(0, 51, 255, 128).ToRawF128());
		Assert.Equal(expected, new SrgbStraight32(0, 51, 255, 128).ToRawF128());
	}

	[Fact]
	public static void ByteTypesShareHexFormatAndParsing() {
		Assert.Equal(new SrgbStraight32(0xcb, 0xa6, 0xf7), SrgbStraight32.Parse("#CBA6F7"));
		Assert.Equal(new RawColor32(0xcb, 0xa6, 0xf7, 0x12), RawColor32.Parse("cba6f712"));
		Assert.False(SrgbStraight32.TryParse("#cba6f", out _));
		Assert.Equal("#CBA6F7", new SrgbStraight32(0xcb, 0xa6, 0xf7).ToHexCode(includeAlpha: false, leadingHash: true));
		Assert.Equal(SrgbStraight32.Green, SrgbStraight32.Parse("00ff00"));
	}

	[Fact]
	public static void SpanReinterpretsAliasAndKeepTheBytes() {
		RawColor32[] raw = [new(0x11, 0x22, 0x33, 0x44), new(0x55, 0x66, 0x77, 0x88)];

		ReadOnlySpan<SrgbStraight32> srgbRo = RawColor32.SpanAssumeSrgbStraight((ReadOnlySpan<RawColor32>)raw);
		Assert.Equal(raw.Length, srgbRo.Length);
		ref byte srgbStart = ref Unsafe.As<SrgbStraight32, byte>(ref MemoryMarshal.GetReference(srgbRo));
		Assert.True(Unsafe.AreSame(ref Unsafe.As<RawColor32, byte>(ref raw[0]), ref srgbStart));
		Assert.Equal((0x55, 0x66, 0x77, 0x88), (srgbRo[1].R, srgbRo[1].G, srgbRo[1].B, srgbRo[1].A));

		// writes through either view land in the same memory
		Span<SrgbStraight32> srgb = RawColor32.SpanAssumeSrgbStraight(raw.AsSpan());
		srgb[0] = new SrgbStraight32(0xaa, 0xbb, 0xcc, 0xdd);
		Assert.Equal(new RawColor32(0xaa, 0xbb, 0xcc, 0xdd), raw[0]);
		Span<RawColor32> back = SrgbStraight32.SpanToRaw(srgb);
		Assert.Equal(raw.Length, back.Length);
		back[1] = new RawColor32(0x01, 0x02, 0x03, 0x04);
		Assert.Equal(new SrgbStraight32(0x01, 0x02, 0x03, 0x04), srgb[1]);

		ReadOnlySpan<RawColor32> backRo = SrgbStraight32.SpanToRaw((ReadOnlySpan<SrgbStraight32>)srgb);
		Assert.True(Unsafe.AreSame(ref raw[0], ref MemoryMarshal.GetReference(backRo)));
		Assert.Equal(raw, backRo.ToArray());

		Assert.True(RawColor32.SpanAssumeSrgbStraight(Span<RawColor32>.Empty).IsEmpty);
		Assert.True(SrgbStraight32.SpanToRaw(ReadOnlySpan<SrgbStraight32>.Empty).IsEmpty);
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
