// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using Injure.Primitives;

namespace Injure.Tests.Primitives;

public sealed class ColorTypeTests {
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
	public static void LayoutsAreFixed() {
		Assert.Equal(RawColor32.Size, Unsafe.SizeOf<RawColor32>());
		Assert.Equal(SrgbColor32.Size, Unsafe.SizeOf<SrgbColor32>());
		Assert.Equal(RawColorF128.Size, Unsafe.SizeOf<RawColorF128>());
		Assert.Equal(0x44332211u, new SrgbColor32(0x11, 0x22, 0x33, 0x44).ReinterpretToU32());
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
