// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Injure.Primitives;

namespace Injure.Tests.Primitives;

[JsonSerializable(typeof(RectF))]
[JsonSerializable(typeof(RectI))]
[JsonSerializable(typeof(Vector2Int))]
[JsonSerializable(typeof(RawColor32))]
[JsonSerializable(typeof(SrgbStraight32))]
internal sealed partial class PrimitivesJsonContext : JsonSerializerContext;

public static class PrimitivesJsonAndFormatTests {
	private static JsonTypeInfo<T> info<T>() => (JsonTypeInfo<T>)PrimitivesJsonContext.Default.GetTypeInfo(typeof(T))!;
	private static string ser<T>(T v) => JsonSerializer.Serialize(v, info<T>());
	private static T de<T>(string json) => JsonSerializer.Deserialize(json, info<T>())!;

	// a culture with a decimal comma and an odd negative sign, to catch formatting that follows the
	// current culture; built by hand, since tests run in globalization-invariant mode
	private static readonly CultureInfo odd = makeOdd();

	private static CultureInfo makeOdd() {
		var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
		c.NumberFormat.NumberDecimalSeparator = ",";
		c.NumberFormat.NegativeSign = "~";
		return c;
	}

	private static string inOddCulture(Func<string> f) {
		CultureInfo old = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = odd;
		try {
			return f();
		} finally {
			CultureInfo.CurrentCulture = old;
		}
	}

	[Fact]
	public static void RectsAndVectorsFormatInTheInvariantCulture() {
		RectF r = new(1.5f, 2f, 3.25f, 4f);
		Assert.Equal("{ X = 1.5, Y = 2, Width = 3.25, Height = 4 }", inOddCulture(r.ToString));
		Assert.Equal("{ X = 1.50, Y = 2.00, Width = 3.25, Height = 4.00 }", inOddCulture(() => r.ToString("F2", odd)));
		Assert.Equal("{ X = -1, Y = 2, Width = 3, Height = 4 }", inOddCulture(() => new RectI(-1, 2, 3, 4).ToString()));
		Assert.Equal("{ X = 1000, Y = -2 }", inOddCulture(() => new Vector2Int(1000, -2).ToString("D", null)));
	}

	[Fact]
	public static void ColorsFormatWithALeadingHashByDefault() {
		SrgbStraight32 c = new(0xcb, 0xa6, 0xf7);
		Assert.Equal("#CBA6F7FF", c.ToString());
		Assert.Equal("#CBA6F7FF", c.ToHexCode());
		Assert.Equal("CBA6F7", c.ToHexCode(includeAlpha: false, leadingHash: false));
		Assert.Equal("#11223344", new RawColor32(0x11, 0x22, 0x33, 0x44).ToString());
	}

	[Fact]
	public static void ColorsRoundTripThroughJsonAsHexStrings() {
		SrgbStraight32 c = new(0xcb, 0xa6, 0xf7, 0x80);
		string json = ser(c);
		Assert.Equal("\"#CBA6F780\"", json);
		Assert.Equal(c, de<SrgbStraight32>(json));
		Assert.Equal(new RawColor32(1, 2, 3, 4), de<RawColor32>(ser(new RawColor32(1, 2, 3, 4))));
		// the same inputs Parse accepts
		Assert.Equal(new SrgbStraight32(0xcb, 0xa6, 0xf7), de<SrgbStraight32>("\"cba6f7\""));
	}

	[Theory]
	[InlineData("\"#cba6f\"")]
	[InlineData("\"nope\"")]
	[InlineData("123")]
	[InlineData("{}")]
	public static void InvalidColorJsonIsRejected(string json) =>
		Assert.Throws<JsonException>(() => de<SrgbStraight32>(json));

	[Fact]
	public static void RectsAndVectorsRoundTripThroughJson() {
		RectF f = new(1.5f, 2f, 3f, 4f);
		Assert.Equal(f, de<RectF>(ser(f)));
		RectI i = new(-1, 2, 3, 4);
		Assert.Equal("{\"x\":-1,\"y\":2,\"width\":3,\"height\":4}", ser(i));
		Assert.Equal(i, de<RectI>(ser(i)));
		Vector2Int v = new(5, -6);
		Assert.Equal("{\"x\":5,\"y\":-6}", ser(v));
		Assert.Equal(v, de<Vector2Int>(ser(v)));
	}

	[Theory]
	[InlineData("{\"x\":0,\"y\":0,\"width\":-1,\"height\":0}")]
	[InlineData("{\"x\":0,\"y\":0,\"width\":1.5,\"height\":0}")]
	[InlineData("{\"x\":0,\"y\":0,\"width\":\"1\",\"height\":0}")]
	[InlineData("{\"x\":0,\"y\":0,\"width\":1}")]
	[InlineData("{\"x\":0,\"y\":0,\"width\":1,\"height\":1,\"z\":0}")]
	[InlineData("[0,0,1,1]")]
	public static void InvalidRectIJsonIsRejected(string json) =>
		Assert.Throws<JsonException>(() => de<RectI>(json));

	[Theory]
	[InlineData("{\"x\":0,\"y\":0,\"width\":-1,\"height\":0}")]
	[InlineData("{\"x\":0,\"y\":0,\"width\":1e39,\"height\":0}")]
	[InlineData("{\"x\":3e38,\"y\":0,\"width\":3e38,\"height\":0}")]
	public static void InvalidRectFJsonIsRejected(string json) =>
		Assert.Throws<JsonException>(() => de<RectF>(json));

	[Fact]
	public static void Vector2IntArithmeticIsPerComponent() {
		Vector2Int a = new(7, -7);
		Vector2Int b = new(2, 3);
		Assert.Equal(new Vector2Int(9, -4), a + b);
		Assert.Equal(new Vector2Int(5, -10), a - b);
		Assert.Equal(new Vector2Int(-7, 7), -a);
		Assert.Equal(new Vector2Int(14, -21), a * b);
		Assert.Equal(new Vector2Int(14, -14), a * 2);
		Assert.Equal(new Vector2Int(14, -14), 2 * a);
		// rounding towards zero, like int division
		Assert.Equal(new Vector2Int(3, -2), a / b);
		Assert.Equal(new Vector2Int(3, -3), a / 2);
		Assert.Equal(new Vector2Int(2, -7), Vector2Int.Min(a, b));
		Assert.Equal(new Vector2Int(7, 3), Vector2Int.Max(a, b));
		Assert.Equal(new Vector2Int(7, 7), Vector2Int.Abs(a));
		(int x, int y) = a;
		Assert.Equal((7, -7), (x, y));
	}

	[Fact]
	public static void Vector2IntConvertsFromVector2WithTheGivenRounding() {
		Vector2 v = new(1.5f, -1.5f);
		Assert.Equal(new Vector2Int(1, -2), Vector2Int.Floor(v));
		Assert.Equal(new Vector2Int(2, -1), Vector2Int.Ceiling(v));
		Assert.Equal(new Vector2Int(1, -1), Vector2Int.Truncate(v));
		Assert.Equal(new Vector2Int(2, -2), Vector2Int.Round(v, MidpointRounding.AwayFromZero));
		Assert.Equal(new Vector2Int(2, -2), Vector2Int.Round(v, MidpointRounding.ToEven));
		Assert.Equal(new Vector2Int(2, -1), Vector2Int.Round(v, MidpointRounding.ToPositiveInfinity));
		Assert.Equal(new Vector2(3, -4), new Vector2Int(3, -4).ToVector2Lossy());
		Assert.Throws<OverflowException>(() => Vector2Int.Floor(new Vector2(float.NaN, 0)));
		Assert.Throws<OverflowException>(() => Vector2Int.Floor(new Vector2(3e9f, 0)));
	}
}
